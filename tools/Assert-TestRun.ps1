#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [ValidateRange(1,1000000)][int]$MinimumTests = 1
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!(Test-Path -LiteralPath $ResultsDirectory -PathType Container)) { throw "No test result directory was produced: $ResultsDirectory" }
$reports = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File)
if ($reports.Count -ne 1) { throw "Expected one actual TRX test run, found $($reports.Count): $ResultsDirectory" }
$settings = [Xml.XmlReaderSettings]::new(); $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit; $settings.XmlResolver = $null; $settings.MaxCharactersInDocument = 32MB
$reader = [Xml.XmlReader]::Create($reports[0].FullName,$settings)
try { $document = [Xml.XmlDocument]::new(); $document.XmlResolver = $null; $document.Load($reader) }
finally { $reader.Dispose() }
$namespaces = [Xml.XmlNamespaceManager]::new($document.NameTable); $namespaces.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$counters = $document.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters',$namespaces)
$summary = $document.SelectSingleNode('/t:TestRun/t:ResultSummary',$namespaces)
if (!$counters -or !$summary) { throw 'TRX summary/counters are missing.' }
$reportText = $document.DocumentElement.InnerText
if ($reportText -match '(?m)^(ERROR:|SCRIPT ERROR:|FATAL:)') { throw 'TRX contains native engine/script/fatal diagnostics despite its counters.' }
if ($reportText -match 'Detected\s*<\s*[1-9][0-9]*\s*>\s+orphan nodes' -or $reportText -match $script:NativeLeakPattern) { throw 'TRX contains native leak/orphan diagnostics despite its counters.' }
if ($summary.GetAttribute('outcome') -notin @('Completed','Passed')) { throw 'TRX reports a failed or aborted run.' }
$total = [int]$counters.GetAttribute('total'); $executed = [int]$counters.GetAttribute('executed'); $passed = [int]$counters.GetAttribute('passed')
$results = @($document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult',$namespaces))
if ($total -lt $MinimumTests -or $executed -ne $total -or $passed -ne $total -or $results.Count -ne $total -or @($results | Where-Object { $_.GetAttribute('outcome') -ne 'Passed' }).Count -ne 0) { throw "Incomplete/failed test run: total=$total executed=$executed passed=$passed required=$MinimumTests" }
Write-Host "Actual test run passed: $passed cases, no skips ($($reports[0].FullName))"
