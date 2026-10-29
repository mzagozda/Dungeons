#requires -Version 7.0
[CmdletBinding()]
param([string]$Executable,[string]$EvidenceDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$Executable) { $Executable = Join-Path $script:RepoRoot 'artifacts/windows/BlockDungeons.exe' }
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/soak/contracts-' + [guid]::NewGuid().ToString('N')) }
$exe = (Resolve-Path -LiteralPath $Executable).Path; $evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Soak contract evidence must use a new directory.' }
[void][IO.Directory]::CreateDirectory($evidence)
$replays = Join-Path $script:RepoRoot 'artifacts/m6/candidates'
$summary = [ordered]@{passed=$false;negativeArgumentCases=0;abortedNativeProcessRejected=$false;soakAcceptance=$false}
function Check-Rejected([string]$Name,[string]$Output,[string]$ReplayPath,[string[]]$Extra,[string]$Expected) {
    $log = Join-Path $evidence ($Name + '.log'); $failed = $false
    $args = @('--headless','--audio-driver','Dummy','--','--qa-mode=soak',"--soak-output=$Output","--soak-replays=$ReplayPath",'--soak-fast') + $Extra
    try { Invoke-Checked $exe $args -TimeoutSeconds 30 -LogPath $log -Quiet } catch { $failed = $true }
    if (!$failed -or [IO.File]::ReadAllText($log) -notmatch $Expected) { throw "Unexpected soak rejection: $Name" }
    $summary.negativeArgumentCases++
}
$controller = $null; $started = $false; $stdout = $null; $stderr = $null; $native = $null
try {
    $keep = Join-Path $evidence 'keep'; [void][IO.Directory]::CreateDirectory($keep)
    $sentinel = Join-Path $keep 'sentinel.txt'; [IO.File]::WriteAllText($sentinel,'preserve this')
    Check-Rejected 'existing-directory' $keep $replays @('--soak-seconds=10') 'must be new'
    Check-Rejected 'existing-file' $sentinel $replays @('--soak-seconds=10') 'must be new'
    $absent = Join-Path $evidence 'never-created'
    Check-Rejected 'short-invalid-duration' $absent $replays @('--soak-seconds=9') '10..28800'
    Check-Rejected 'unknown-flag' $absent $replays @('--soak-seconds=10','--soak-typo=true') 'Unknown soak argument'
    $missingOutput = Join-Path $evidence 'missing-replay'
    Check-Rejected 'missing-replay' $missingOutput (Join-Path $evidence 'absent-replays') @('--soak-seconds=10') 'Cannot read'
    $failedInit = Get-Content -LiteralPath (Join-Path $missingOutput 'result.json') -Raw | ConvertFrom-Json
    if ($failedInit.passed -or $failedInit.soakAcceptance -or (Test-Path -LiteralPath $absent) -or [IO.File]::ReadAllText($sentinel) -ne 'preserve this') { throw 'Failed initialization or output preservation contract failed.' }

    # Kill only the private native process started by this controller, after its
    # heartbeat proves real simulation. This must never produce a passing soak.
    $aborted = Join-Path $evidence 'aborted'
    $info = [Diagnostics.ProcessStartInfo]::new('pwsh'); $info.WorkingDirectory = $script:RepoRoot
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($arg in @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Test-DesktopSoak.ps1'),'-Executable',$exe,'-Seconds','60','-Fast','-EvidenceDirectory',$aborted)) { $info.ArgumentList.Add($arg) }
    $controller = [Diagnostics.Process]::new(); $controller.StartInfo = $info
    if (!$controller.Start()) { throw 'Could not start the crash-rejection controller.' }
    $started = $true; $stdout = $controller.StandardOutput.ReadToEndAsync(); $stderr = $controller.StandardError.ReadToEndAsync()
    $deadline = [Diagnostics.Stopwatch]::StartNew(); $record = $null; $ready = $false
    while (!$controller.WaitForExit(500) -and $deadline.Elapsed.TotalSeconds -lt 40) {
        $processFile = Join-Path $aborted 'process.json'; $heartbeatFile = Join-Path $aborted 'run/heartbeat.json'
        if ((Test-Path -LiteralPath $processFile) -and (Test-Path -LiteralPath $heartbeatFile)) {
            $record = Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json
            try { $beat = Get-Content -LiteralPath $heartbeatFile -Raw | ConvertFrom-Json } catch [IO.IOException] { continue }
            if ($beat.processId -eq $record.processId -and $beat.progress.SimulatedTicks -gt 0) { $ready = $true; break }
        }
    }
    if (!$ready) { throw 'Fault-injection process never reached live simulation.' }
    $native = [Diagnostics.Process]::GetProcessById($record.processId)
    $actualExe = [IO.Path]::GetFullPath($native.MainModule.FileName); $expectedExe = [IO.Path]::GetFullPath($record.executable)
    $privateRoot = [IO.Path]::GetFullPath((Join-Path $aborted 'bin')) + [IO.Path]::DirectorySeparatorChar
    if (!$actualExe.Equals($expectedExe,[StringComparison]::OrdinalIgnoreCase) -or !$actualExe.StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase) -or [Math]::Abs(($native.StartTime.ToUniversalTime() - ([DateTimeOffset]$record.startUtc).UtcDateTime).TotalSeconds) -gt 1) { throw 'Refusing to terminate a process with mismatched ownership.' }
    $native.Kill(); $native.WaitForExit(); $native.Dispose(); $native = $null
    if (!$controller.WaitForExit(30000)) { throw 'Controller did not reject its dead native process.' }
    [IO.File]::WriteAllText((Join-Path $evidence 'aborted-controller.log'),($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()))
    $abortedResult = Get-Content -LiteralPath (Join-Path $aborted 'result.json') -Raw | ConvertFrom-Json
    if ($controller.ExitCode -eq 0 -or $abortedResult.passed -or $abortedResult.soakAcceptance) { throw 'A killed native process was incorrectly accepted.' }
    $summary.abortedNativeProcessRejected = $true; $summary.abortedProcessId = $record.processId; $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    if ($native) { $native.Dispose() }
    if ($controller) { if ($started -and !$controller.HasExited) { $controller.Kill($true); $controller.WaitForExit() }; $controller.Dispose() }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Write-Host "Desktop soak failure contracts: $evidence (passed: $($summary.passed))"
}
