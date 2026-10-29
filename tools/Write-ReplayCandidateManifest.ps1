[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$candidateDirectory = (Resolve-Path -LiteralPath $Directory).Path
$entries = @(Get-ChildItem -LiteralPath $candidateDirectory -Filter '*.dreplay' -File | Sort-Object Name | ForEach-Object {
    $replay = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    if ($replay.tickCount -ne @($replay.tickHashes).Count) { throw "Replay hash count mismatch: $($_.Name)" }
    [ordered]@{
        name = $_.Name; tickCount = $replay.tickCount; seed = $replay.seed
        initialHash = $replay.initialHash
        finalHash = $replay.finalHash
        levelHash = $replay.levelHash; contentHash = $replay.contentHash
        fileSha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
if ($entries.Count -eq 0) { throw 'No replay candidates were found.' }
[ordered]@{ reviewStatus = 'pending'; committedGolden = $false; candidates = $entries } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $candidateDirectory 'review-manifest.json') -Encoding UTF8
Write-Host "Recorded $($entries.Count) candidate identities; review remains pending."
