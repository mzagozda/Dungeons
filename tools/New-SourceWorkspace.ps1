#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkspaceDirectory,
    [Parameter(Mandatory)][string]$ManifestPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$workspace = [IO.Path]::GetFullPath($WorkspaceDirectory)
$manifest = [IO.Path]::GetFullPath($ManifestPath)
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
foreach ($target in @($workspace,$manifest)) {
    if (!$target.StartsWith($artifactRoot,$comparison) -or (Test-Path -LiteralPath $target)) { throw 'Source workspace and manifest must be new paths under repository artifacts.' }
}
if ($manifest.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar,$comparison)) { throw 'Source manifest must be outside the copied workspace.' }
$files = @(git -C $script:RepoRoot -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Cannot enumerate repository source for the isolated project.' }
[void][IO.Directory]::CreateDirectory($workspace)
$sourcePrefix = [IO.Path]::GetFullPath($script:RepoRoot) + [IO.Path]::DirectorySeparatorChar
$destinationPrefix = $workspace + [IO.Path]::DirectorySeparatorChar
$identities = foreach ($relative in $files) {
    $sourceFile = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot $relative))
    $target = [IO.Path]::GetFullPath((Join-Path $workspace $relative))
    if (!$sourceFile.StartsWith($sourcePrefix,$comparison) -or !$target.StartsWith($destinationPrefix,$comparison)) { throw 'Source copy escaped the repository or isolated workspace.' }
    # A tracked deletion is absent from the current checkout and stays absent.
    if (!(Test-Path -LiteralPath $sourceFile)) { continue }
    $item = Get-Item -LiteralPath $sourceFile -Force
    if ($item.PSIsContainer -or $item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "Source snapshot requires regular files: $relative" }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    Copy-Item -LiteralPath $sourceFile -Destination $target
    $sha = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $sha) { throw "Source copy differs: $relative" }
    [ordered]@{path=$relative;sha256=$sha}
}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($manifest))
$identities | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifest -Encoding utf8
[pscustomobject]@{Workspace=$workspace;Manifest=$manifest;Files=$identities.Count}
