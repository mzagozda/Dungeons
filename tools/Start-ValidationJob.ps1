#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('DesktopSoak','M9Rendered','Benchmarks')][string]$Kind = 'DesktopSoak',
    [ValidatePattern('^[a-z][a-z0-9-]{0,63}$')][string]$BenchmarkRunner,
    [string]$BenchmarkBaselineFile,
    [string]$Executable,
    [string]$ReplayDirectory,
    [ValidateRange(10,28800)][int]$Seconds = 7200,
    [ValidateSet('mobile','gl_compatibility')][string]$Renderer = 'mobile',
    [switch]$Fast,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$IsWindows) { throw 'This detached launcher uses the local Windows process provider.' }
if ($Kind -eq 'Benchmarks' -and !$BenchmarkRunner) { throw 'Benchmarks require an explicit -BenchmarkRunner baseline identity.' }
if ($BenchmarkBaselineFile -and $Kind -ne 'Benchmarks') { throw '-BenchmarkBaselineFile requires a Benchmarks job.' }
$benchmarkBaseline = if ($BenchmarkBaselineFile) { (Resolve-Path -LiteralPath $BenchmarkBaselineFile).Path } else { $null }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory); $jobRoot = $evidence + '.launch'
if ((Test-Path -LiteralPath $evidence) -or (Test-Path -LiteralPath $jobRoot)) { throw 'Detached validation evidence and launch directories must be new.' }
$exe = $null; $replays = $null
if ($Kind -eq 'DesktopSoak') {
    if (!$Executable) { $Executable = Join-Path $script:RepoRoot 'artifacts/windows/BlockDungeons.exe' }
    if (!$ReplayDirectory) { $ReplayDirectory = Join-Path $script:RepoRoot 'artifacts/m6/candidates' }
    $exe = (Resolve-Path -LiteralPath $Executable).Path; $replays = (Resolve-Path -LiteralPath $ReplayDirectory).Path
}
[void][IO.Directory]::CreateDirectory($jobRoot)
$requestPath = Join-Path $jobRoot 'request.json'
[ordered]@{kind=$Kind;repoRoot=$script:RepoRoot;executable=$exe;replays=$replays;seconds=$Seconds;renderer=$Renderer;fast=$Fast.IsPresent;evidence=$evidence;benchmarkRunner=$BenchmarkRunner;benchmarkBaselineFile=$benchmarkBaseline} | ConvertTo-Json | Set-Content -LiteralPath $requestPath -Encoding utf8
$runner = Join-Path $PSScriptRoot 'Run-ValidationJob.ps1'; $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
Copy-Item -LiteralPath $runner -Destination (Join-Path $jobRoot 'runner-source.ps1')
$command = '"{0}" -NoProfile -NonInteractive -File "{1}" -RequestFile "{2}"' -f $pwsh,$runner,$requestPath
# Use the local process provider so the supervisor belongs to its own process
# lifetime rather than the calling terminal. New console is hidden; no recurring
# task, service, global power setting or elevation is installed.
# https://learn.microsoft.com/windows/win32/cimwin32prov/create-method-in-class-win32-process
$startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ShowWindow=[uint16]0;CreateFlags=[uint32]16777232}
$created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=$command;CurrentDirectory=$script:RepoRoot;ProcessStartupInformation=$startup}
if ($created.ReturnValue -ne 0) { throw "Detached validation process creation failed with Windows code $($created.ReturnValue)." }
$process = Get-Process -Id $created.ProcessId -ErrorAction Stop
[ordered]@{processId=$process.Id;startUtc=$process.StartTime.ToUniversalTime().ToString('o');sessionId=$process.SessionId;callerSessionId=(Get-Process -Id $PID).SessionId;kind=$Kind;evidence=$evidence;requestFile=$requestPath;runnerSha256=(Get-FileHash -LiteralPath $runner).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $jobRoot 'launch.json') -Encoding utf8
Write-Host "Detached validation started: PID $($process.Id); logs: $jobRoot"
