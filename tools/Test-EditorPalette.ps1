[CmdletBinding()]
param([string]$EvidenceDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/m9/palette-cli-' + [guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($EvidenceDirectory)
$evidence = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
Push-Location $script:RepoRoot
try {
    $level = Join-Path $script:RepoRoot 'game/levels/mossy_hollow/level.json'
    $source = [IO.File]::ReadAllText($level) | ConvertFrom-Json
    function Read-Board([string]$Path) {
        $lines = [IO.File]::ReadAllLines($Path); $start = [Array]::IndexOf($lines, [string]$source.name)
        if ($start -lt 0 -or $lines.Count -lt $start + 1 + $source.size.height) { throw 'ASCII preview lacks the complete level.' }
        $rows = $lines[($start + 1)..($start + $source.size.height)]
        if (@($rows | Where-Object Length -ne $source.size.width).Count) { throw 'ASCII preview row dimensions differ.' }
        return $rows -join "`n"
    }
    $baseline = Join-Path $evidence 'baseline.log'
    Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','preview',$level) -LogPath $baseline -Quiet
    $board = Read-Board $baseline
    if ($board -notmatch 'Z' -or $board -notmatch 'S' -or $board -notmatch 'P' -or $board -notmatch 'X') { throw 'Authored entity palette characters are missing.' }
    $custom = Join-Path $evidence 'custom content'; Copy-Item -LiteralPath 'game/content' -Destination $custom -Recurse
    foreach ($change in @(@('entities/zombie.json','Q'), @('tiles/grass.json','g'))) {
        $file = Join-Path $custom $change[0]; $definition = [IO.File]::ReadAllText($file) | ConvertFrom-Json
        $definition.editor.char = $change[1]
        [IO.File]::WriteAllText($file, (($definition | ConvertTo-Json -Depth 100).Replace("`r`n","`n") + "`n"))
    }
    $changed = Join-Path $evidence 'custom.log'
    Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','preview',$level,'--content',$custom) -LogPath $changed -Quiet
    $board = Read-Board $changed
    if ($board -notmatch 'Q' -or $board -notmatch 'g' -or $board -match 'Z') { throw 'ASCII preview did not use the changed registry hints.' }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); defaultPalettePassed=$true; customTileAndEntityCharactersPassed=$true; width=$source.size.width; height=$source.size.height } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "Data-driven ASCII palette checks passed: $evidence"
}
finally { Pop-Location }
