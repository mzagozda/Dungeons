#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ToolsBinary, [Parameter(Mandatory)][string]$EvidenceDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$binary = (Resolve-Path -LiteralPath $ToolsBinary).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'CLI contract evidence must use a new directory.' }
[void][IO.Directory]::CreateDirectory($evidence)
Push-Location $script:RepoRoot
try {
    function Assert-FuzzRejected([string]$Name, [string[]]$Extra, [string]$Message) {
        $log = Join-Path $evidence ($Name + '.log'); $rejected = $false
        try { Invoke-Checked dotnet (@($binary,'sim-fuzz','game/levels/mossy_hollow/level.json') + $Extra) -LogPath $log -Quiet }
        catch { $rejected = $true }
        if (!$rejected -or [IO.File]::ReadAllText($log) -notmatch $Message) { throw "Unexpected CLI boundary result: $Name" }
    }
    $sentinel = Join-Path $evidence 'preserve.txt'; [IO.File]::WriteAllText($sentinel,'preserved evidence')
    Assert-FuzzRejected 'existing-directory' @($evidence,'2','10000') 'must be new'
    Assert-FuzzRejected 'existing-file' @($sentinel,'2','10000') 'must be new'
    $absent = Join-Path $evidence 'never-created'
    Assert-FuzzRejected 'invalid-cases' @($absent,'1','10000') '2..128'
    Assert-FuzzRejected 'invalid-ticks' @($absent,'2','0') '1..100000'
    if ([IO.File]::ReadAllText($sentinel) -ne 'preserved evidence' -or (Test-Path -LiteralPath $absent)) { throw 'CLI boundary changed preserved files.' }
    Invoke-Checked dotnet @($binary,'sim-fuzz','game/levels/mossy_hollow/level.json',(Join-Path $evidence 'short'),'2','4') -LogPath (Join-Path $evidence 'short.log') -Quiet
    $short = Get-Content -LiteralPath (Join-Path $evidence 'short/result.json') -Raw | ConvertFrom-Json
    if (!$short.passed -or $short.simulationFuzzAcceptance -or $short.actualTicksAcrossBothRuns -ne 16) { throw 'Short CLI run misrepresented acceptance.' }
    [ordered]@{passed=$true;negativeCases=4;shortRunAcceptance=$false;physicalAcceptance=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Write-Host 'Five simulation fuzz CLI boundary contracts passed.'
}
finally { Pop-Location }
