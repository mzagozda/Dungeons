#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
. (Join-Path $PSScriptRoot 'ReleaseArtifacts.ps1')
if (!$IsWindows) { throw 'Installer validation requires Windows.' }
if (Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F2F3411-E009-4A65-AE08-195AB82B3FA6}_is1') { throw 'An existing Block Dungeons installation is registered; refusing to replace it for QA.' }
$artifacts = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$info = Get-Content -LiteralPath (Join-Path $artifacts 'build-info.json') -Raw | ConvertFrom-Json
if (!$info.passed) { throw 'Windows packaging did not pass.' }
$identity = $info.identity
$installer = Join-Path $artifacts ($identity.stem + '-windows-x64-setup.exe')
$zip = Join-Path $artifacts ($identity.stem + '-windows-x64-portable.zip')
$evidence = New-ReleaseDirectory $EvidenceDirectory
$portable = Join-Path $evidence 'portable'
$installed = Join-Path $evidence 'installed'
$summary = [ordered]@{passed=$false;portableLaunch=$false;installerLaunch=$false;payloadParity=$false;uninstall=$false;physicalAcceptance=$false}
try {
    foreach ($line in Get-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt')) {
        if ($line -notmatch '^([a-f0-9]{64})  ([a-zA-Z0-9._-]+)$' -or (Get-FileHash -LiteralPath (Join-Path $artifacts $Matches[2]) -Algorithm SHA256).Hash -ine $Matches[1]) { throw 'Release artifact checksum mismatch.' }
    }
    [IO.Compression.ZipFile]::ExtractToDirectory($zip,$portable)
    $files = @(Get-ChildItem -LiteralPath $portable -File -Recurse)
    if ($files.Count -lt 10 -or @($files | Where-Object Extension -In @('.pdb','.log','.trx','.p12','.keystore')).Count) { throw 'Portable payload is incomplete or contains build/signing debris.' }
    Invoke-Probe -Executable (Join-Path $portable 'BlockDungeons.exe') -Arguments @('--headless','--audio-driver','Dummy','--','--m0-smoke-test') -LogPath (Join-Path $evidence 'portable-smoke.log') -Packaged
    $summary.portableLaunch = $true
    # Inno accepts one /DIR= argument containing spaces. No shell is involved.
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=','/NOICONS',('/DIR="' + $installed + '"'),('/LOG="' + (Join-Path $evidence 'install.log') + '"')) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Installer timed out.' }
    if ($process.ExitCode -ne 0) { throw "Installer failed with exit code $($process.ExitCode)." }
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($portable,$file.FullName)
        $target = Join-Path $installed $relative
        if (!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "Installer/portable payload mismatch: $relative" }
    }
    $summary.payloadParity = $true
    Invoke-Probe -Executable (Join-Path $installed 'BlockDungeons.exe') -Arguments @('--headless','--audio-driver','Dummy','--','--m0-smoke-test') -LogPath (Join-Path $evidence 'installed-smoke.log') -Packaged
    $summary.installerLaunch = $true
    if ($info.authenticodeSigned -and (Get-AuthenticodeSignature -LiteralPath (Join-Path $installed 'unins000.exe')).Status -ne 'Valid') { throw 'Signed build has an invalid uninstaller signature.' }
} finally {
    $uninstaller = Join-Path $installed 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $evidence 'uninstall.log') + '"')) -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Uninstaller timed out.' }
        # Inno's bootstrap can return before its temporary child; bound filesystem completion.
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while ((Test-Path -LiteralPath (Join-Path $installed 'BlockDungeons.exe')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
        $summary.uninstall = $process.ExitCode -eq 0 -and !(Test-Path -LiteralPath (Join-Path $installed 'BlockDungeons.exe'))
    }
    $summary.passed = $summary.portableLaunch -and $summary.installerLaunch -and $summary.payloadParity -and $summary.uninstall
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
}
if (!$summary.passed) { throw 'Windows portable/install/uninstall validation failed.' }
Write-Host 'Windows portable launch, installed payload/launch and uninstall passed.'
