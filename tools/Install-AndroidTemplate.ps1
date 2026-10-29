[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'Common.ps1')
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $dataDirectory = Join-Path $env:APPDATA 'Godot' }
elseif ($IsMacOS) { $dataDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Library/Application Support/Godot' }
elseif ($env:XDG_DATA_HOME) { $dataDirectory = Join-Path $env:XDG_DATA_HOME 'godot' }
else { $dataDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.local/share/godot' }
$version = $script:Toolchain.templateDirectory
$archive = Join-Path $dataDirectory "export_templates/$version/android_source.zip"
if (!(Test-Path -LiteralPath $archive)) { throw 'Install the pinned Mono export templates with Install-Godot.ps1 -WithExportTemplates first.' }
$androidDirectory = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'game/android'))
$buildDirectory = Join-Path $androidDirectory 'build'
$marker = Join-Path $androidDirectory '.build_version'
if ((Test-Path -LiteralPath $marker) -and ([IO.File]::ReadAllText($marker)).Trim() -eq $version -and (Test-Path -LiteralPath (Join-Path $buildDirectory 'build.gradle'))) { return }
if (Test-Path -LiteralPath $buildDirectory) {
    # Keep all prior generated/customized template files for recovery.
    $history = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot ('artifacts/android-template-history/' + [guid]::NewGuid().ToString('N'))))
    $workspace = [IO.Path]::GetFullPath($script:RepoRoot) + [IO.Path]::DirectorySeparatorChar
    if ($buildDirectory -ne [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'game/android/build')) -or !$history.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase)) { throw 'Template archive paths escaped the workspace.' }
    [void][IO.Directory]::CreateDirectory($history)
    if (Test-Path -LiteralPath $marker) { Copy-Item -LiteralPath $marker -Destination $history }
    Move-Item -LiteralPath $buildDirectory -Destination (Join-Path $history 'build')
    Write-Host "Prior Android template preserved in $history"
}
[void][IO.Directory]::CreateDirectory($buildDirectory)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $allowedRoot = [IO.Path]::GetFullPath($buildDirectory) + [IO.Path]::DirectorySeparatorChar
    foreach ($entry in $zip.Entries) {
        if (!$entry.Name) { continue }
        $target = [IO.Path]::GetFullPath((Join-Path $buildDirectory $entry.FullName))
        if (!$target.StartsWith($allowedRoot, [StringComparison]::Ordinal)) { throw 'Unsafe Android template archive entry.' }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
}
finally { $zip.Dispose() }
[IO.File]::WriteAllText((Join-Path $androidDirectory '.gdignore'), '')
[IO.File]::WriteAllText($marker, $version + "`n", [Text.UTF8Encoding]::new($false))
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { Invoke-Checked chmod @('+x', (Join-Path $buildDirectory 'gradlew')) }
Write-Host "Installed Android source template $version."
