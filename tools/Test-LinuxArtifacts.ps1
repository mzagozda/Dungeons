#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
. (Join-Path $PSScriptRoot 'ReleaseArtifacts.ps1')
if (!$IsLinux) { throw 'Debian package installation validation requires Linux.' }
$artifacts = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$info = Get-Content -LiteralPath (Join-Path $artifacts 'build-info.json') -Raw | ConvertFrom-Json
if (!$info.passed) { throw 'Linux packaging did not pass.' }
$identity = $info.identity
$package = Join-Path $artifacts ($identity.stem + '-linux-amd64.deb')
$installed = & dpkg-query --show '--showformat=${Status}' block-dungeons 2>$null
if ($LASTEXITCODE -eq 0 -and $installed -match 'install ok installed') { throw 'Refusing to replace an existing Block Dungeons installation.' }
$evidence = New-ReleaseDirectory $EvidenceDirectory
$summary = [ordered]@{passed=$false;installed=$false;payloadParity=$false;launch=$false;uninstall=$false;physicalAcceptance=$false}
try {
    foreach ($line in Get-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt')) {
        if ($line -notmatch '^([a-f0-9]{64})  ([a-zA-Z0-9._-]+)$' -or (Get-FileHash -LiteralPath (Join-Path $artifacts $Matches[2])).Hash -ine $Matches[1]) { throw 'Linux package checksum mismatch.' }
    }
    Invoke-Checked sudo @('apt-get','update') -LogPath (Join-Path $evidence 'apt-update.log') -Quiet
    Invoke-Checked sudo @('apt-get','install','--yes','--no-install-recommends',$package) -LogPath (Join-Path $evidence 'install.log') -Quiet
    $summary.installed = $true
    $version = (& dpkg-query --show '--showformat=${Version}' block-dungeons | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $version -cne $info.debianVersion) { throw 'Installed Debian version differs from the package identity.' }
    $extracted = Join-Path $evidence 'extracted'
    Invoke-Checked dpkg-deb @('--extract',$package,$extracted) -Quiet
    foreach ($file in Get-ChildItem -LiteralPath $extracted -Recurse -File) {
        $target = '/' + [IO.Path]::GetRelativePath($extracted,$file.FullName)
        if (!(Test-Path -LiteralPath $target -PathType Leaf) -or (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Installed Debian payload differs: $target" }
    }
    $summary.payloadParity = $true
    Invoke-Probe -Executable '/usr/bin/block-dungeons' -Arguments @('--headless','--audio-driver','Dummy','--','--m0-smoke-test') -LogPath (Join-Path $evidence 'installed-smoke.log') -Packaged
    foreach ($mode in @('palette','ui')) {
        $log = Join-Path $evidence ("installed-$mode.log")
        Invoke-Checked '/usr/bin/block-dungeons' @('--headless','--audio-driver','Dummy','--',"--qa-mode=$mode") -LogPath $log -RejectEngineErrors -Quiet
        $marker = if ($mode -eq 'palette') { 'PACKAGED_EDITOR_PALETTE=PASS' } else { 'PACKAGED_UI_THEME=PASS' }
        if ([IO.File]::ReadAllText($log) -notmatch $marker) { throw "Installed Debian $mode assets were not verified." }
    }
    $summary.launch = $true
} finally {
    if ($summary.installed) {
        Invoke-Checked sudo @('dpkg','--remove','block-dungeons') -LogPath (Join-Path $evidence 'uninstall.log') -Quiet
        $summary.uninstall = !(Test-Path -LiteralPath '/usr/bin/block-dungeons') -and !(Test-Path -LiteralPath '/opt/block-dungeons/BlockDungeons.x86_64')
    }
    $summary.passed = $summary.installed -and $summary.payloadParity -and $summary.launch -and $summary.uninstall
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
}
if (!$summary.passed) { throw 'Debian installation, launch or removal validation failed.' }
Write-Host 'Debian package installation, payload parity, smoke/assets and removal passed.'
