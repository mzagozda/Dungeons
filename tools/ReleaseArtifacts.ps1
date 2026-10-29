# Shared release-packaging helpers. Signing material must never enter artifact directories.
. (Join-Path $PSScriptRoot 'Common.ps1')

function Read-ReleaseIdentity {
    param([string]$Path)
    $identity = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($identity.formatVersion -ne 1 -or $identity.versionName -notmatch '^\d+\.\d+\.\d+$' -or
        $identity.commit -notmatch '^[a-f0-9]{40}$' -or $identity.runNumber -lt 1 -or $identity.runAttempt -lt 1 -or
        $identity.androidVersionCode -lt 1 -or $identity.androidVersionCode -gt 2100000000 -or
        $identity.stem -cne "BlockDungeons-$($identity.versionName)-build$($identity.runNumber).$($identity.runAttempt)-$($identity.commit.Substring(0,12))") {
        throw 'Invalid CI release identity.'
    }
    return $identity
}

function New-ReleaseDirectory {
    param([string]$Path)
    $full = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    $prefix = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (!$full.StartsWith($prefix,$comparison) -or (Test-Path -LiteralPath $full)) { throw 'Release directories must be new and under repository artifacts.' }
    [void][IO.Directory]::CreateDirectory($full)
    return $full
}

function Write-ReleaseChecksums {
    param([string]$Directory)
    $lines = @(Get-ChildItem -LiteralPath $Directory -File | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    })
    [IO.File]::WriteAllLines((Join-Path $Directory 'SHA256SUMS.txt'),$lines,[Text.UTF8Encoding]::new($false))
}
