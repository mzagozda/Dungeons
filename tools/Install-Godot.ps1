[CmdletBinding()]
param([switch]$WithExportTemplates)
. (Join-Path $PSScriptRoot 'Common.ps1')

$cacheDir = Join-Path $script:RepoRoot '.toolchain'
[void][IO.Directory]::CreateDirectory($cacheDir)
$isWindowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$isMacHost = !$isWindowsHost -and (Test-Path -LiteralPath '/System/Library/CoreServices/SystemVersion.plist')
if ($isWindowsHost) {
    $platform = 'win64'
    $checksum = $script:Toolchain.windowsSha512
}
elseif ($isMacHost) {
    $platform = 'macos.universal'
    $checksum = $script:Toolchain.macosSha512
}
else {
    $platform = 'linux_x86_64'
    $checksum = $script:Toolchain.linuxSha512
}
$baseUrl = "https://github.com/godotengine/godot-builds/releases/download/$($script:Toolchain.release)"

function Get-VerifiedArchive {
    param([string]$Url, [string]$Path, [string]$Sha512)
    if (!(Test-Path -LiteralPath $Path) -or (Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash -ine $Sha512) {
        Write-Host "Downloading $Url"
        $curlPath = (Get-Command curl -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        Invoke-Checked -FilePath $curlPath -Arguments @('--fail', '--location', '--retry', '3', '--silent', '--show-error', '--output', $Path, $Url)
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash -ine $Sha512) { throw "SHA512 mismatch: $Path" }
}

$archive = Join-Path $cacheDir "godot-$($script:Toolchain.release)-$platform.zip"
Get-VerifiedArchive -Url "$baseUrl/Godot_v$($script:Toolchain.release)_mono_$platform.zip" -Path $archive -Sha512 $checksum
$editorDir = Join-Path $cacheDir "godot/$($script:Toolchain.release)"
Expand-Archive -LiteralPath $archive -DestinationPath $editorDir -Force
if ($isWindowsHost) {
    $binary = Get-ChildItem -LiteralPath $editorDir -Recurse -Filter '*_mono_win64.exe' | Select-Object -First 1 -ExpandProperty FullName
}
elseif ($isMacHost) {
    $binary = Join-Path $editorDir 'Godot_mono.app/Contents/MacOS/Godot'
}
else {
    $binary = Get-ChildItem -LiteralPath $editorDir -Recurse -File -Filter '*mono_linux.x86_64' | Select-Object -First 1 -ExpandProperty FullName
}
if (!$binary -or !(Test-Path -LiteralPath $binary)) { throw 'Cannot find the downloaded Godot .NET editor.' }
if (!$isWindowsHost) { Invoke-Checked -FilePath 'chmod' -Arguments @('+x', $binary) }
Assert-GodotVersion -Path $binary
[IO.File]::WriteAllText((Join-Path $cacheDir 'godot-path.txt'), $binary)

if ($WithExportTemplates) {
    $templatesArchive = Join-Path $cacheDir "templates-$($script:Toolchain.release).tpz"
    Get-VerifiedArchive -Url "$baseUrl/Godot_v$($script:Toolchain.release)_mono_export_templates.tpz" -Path $templatesArchive -Sha512 $script:Toolchain.templatesSha512
    if ($isWindowsHost) { $dataDir = Join-Path $env:APPDATA 'Godot' }
    elseif ($isMacHost) { $dataDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Library/Application Support/Godot' }
    elseif ($env:XDG_DATA_HOME) { $dataDir = Join-Path $env:XDG_DATA_HOME 'godot' }
    else { $dataDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.local/share/godot' }
    $templateDir = Join-Path $dataDir "export_templates/$($script:Toolchain.templateDirectory)"
    [void][IO.Directory]::CreateDirectory($templateDir)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($templatesArchive)
    try {
        foreach ($entry in $zip.Entries) {
            if (!$entry.FullName.StartsWith('templates/', [StringComparison]::Ordinal) -or !$entry.Name) { continue }
            $relative = $entry.FullName.Substring('templates/'.Length)
            $target = [IO.Path]::GetFullPath((Join-Path $templateDir $relative))
            $allowedRoot = [IO.Path]::GetFullPath($templateDir) + [IO.Path]::DirectorySeparatorChar
            if (!$target.StartsWith($allowedRoot, [StringComparison]::Ordinal)) { throw 'Unsafe template entry.' }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
    finally { $zip.Dispose() }
    Write-Host "Export templates installed: $templateDir"
}
Write-Host "Godot installed: $binary"

if ($isWindowsHost -and $WithExportTemplates) {
    $resourceEditor = Join-Path $cacheDir 'rcedit-x64.exe'
    $expected = '3e7801db1a5edbec91b49a24a094aad776cb4515488ea5a4ca2289c400eade2a'
    if (!(Test-Path -LiteralPath $resourceEditor)) {
        $curlPath = (Get-Command curl -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        Invoke-Checked -FilePath $curlPath -Arguments @('--fail', '--location', '--silent', '--show-error', '--output', $resourceEditor, 'https://github.com/electron/rcedit/releases/download/v2.0.0/rcedit-x64.exe')
    }
    if ((Get-FileHash -LiteralPath $resourceEditor -Algorithm SHA256).Hash -ine $expected) { throw 'rcedit SHA256 mismatch.' }
}
