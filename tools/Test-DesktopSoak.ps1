#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Executable,
    [string]$ReplayDirectory,
    [ValidateRange(10,28800)][int]$Seconds = 7200,
    [ValidateSet('mobile','gl_compatibility')][string]$Renderer = 'mobile',
    [switch]$Fast,
    [string]$EvidenceDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$IsWindows) { throw 'This runner audits exported Windows desktop packages. Mobile/device and other OS soaks remain separate.' }
if (!$Executable) { $Executable = Join-Path $script:RepoRoot 'artifacts/windows/BlockDungeons.exe' }
if (!$ReplayDirectory) { $ReplayDirectory = Join-Path $script:RepoRoot 'artifacts/m6/candidates' }
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/soak/windows-' + [guid]::NewGuid().ToString('N')) }
$sourceExe = (Resolve-Path -LiteralPath $Executable).Path
$replays = (Resolve-Path -LiteralPath $ReplayDirectory).Path
$sourceData = Join-Path ([IO.Path]::GetDirectoryName($sourceExe)) 'data_Dungeons.Game_windows_x86_64'
if (!(Test-Path -LiteralPath (Join-Path $sourceData 'Dungeons.Game.dll')) -or !(Test-Path -LiteralPath (Join-Path $sourceData 'Dungeons.Core.dll'))) { throw 'An exported desktop package is required; run tools/Export-M0.ps1 -Platform Windows first.' }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if ($evidence.StartsWith([IO.Path]::GetFullPath($sourceData) + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Soak evidence must be outside the exported payload to avoid recursive copying.' }
if (Test-Path -LiteralPath $evidence) { throw 'Soak evidence must use a new directory.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;soakAcceptance=$false;requestedSeconds=$Seconds;acceleratedDiagnostic=$Fast.IsPresent;renderer=$Renderer;physicalMobileAcceptance=$false;audioListeningAcceptance=$false;reviewedGolden=$false}
$process = $null; $stdout = $null; $stderr = $null; $started = $false; $clock = [Diagnostics.Stopwatch]::new()
$soakPreviousExecutionState = [uint32]0; $soakPowerHeld = $false
function Read-ModuleId([string]$Path) {
    $file = [IO.File]::OpenRead($Path)
    try {
        $pe = [Reflection.PortableExecutable.PEReader]::new($file)
        try { $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe); return $metadata.GetGuid($metadata.GetModuleDefinition().Mvid).ToString() }
        finally { $pe.Dispose() }
    }
    finally { $file.Dispose() }
}
function Read-Heartbeat([string]$Path) {
    $file = [IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try { $reader = [IO.StreamReader]::new($file); try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() } }
    finally { $file.Dispose() }
}
try {
    # Copy the actual exported executable and its complete .NET payload. A long soak
    # never observes subsequent source edits, rebuilds, exports or replay regeneration.
    $bin = Join-Path $evidence 'bin'; [void][IO.Directory]::CreateDirectory($bin)
    $exe = Join-Path $bin ([IO.Path]::GetFileName($sourceExe)); Copy-Item -LiteralPath $sourceExe -Destination $exe
    Copy-Item -LiteralPath $sourceData -Destination (Join-Path $bin 'data_Dungeons.Game_windows_x86_64') -Recurse
    $files = @(Get-ChildItem -LiteralPath $bin -File -Recurse | Sort-Object FullName)
    $identity = foreach ($file in $files) { [ordered]@{path=[IO.Path]::GetRelativePath($bin,$file.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $file.FullName).Hash;bytes=$file.Length} }
    $identity | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'package-files.json') -Encoding utf8
    $gameModule = Read-ModuleId (Join-Path $bin 'data_Dungeons.Game_windows_x86_64/Dungeons.Game.dll')
    $coreModule = Read-ModuleId (Join-Path $bin 'data_Dungeons.Game_windows_x86_64/Dungeons.Core.dll')
    # Temporary thread-scoped idle inhibition for unattended rendered QA. The user
    # can still request sleep/lock; no power-plan setting or away mode is changed.
    # https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate
    if (!('Dungeons.Soaking.Power' -as [type])) {
        Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
namespace Dungeons.Soaking {
    public static class Power {
        [DllImport("kernel32.dll", SetLastError=true)]
        public static extern uint SetThreadExecutionState(uint flags);
    }
}
'@
    }
    $soakPreviousExecutionState = [Dungeons.Soaking.Power]::SetThreadExecutionState([uint32]2147483651)
    if (!$soakPreviousExecutionState) { throw 'Could not acquire the temporary display/system idle request.' }
    $soakPowerHeld = $true; $summary.temporaryKeepAwakeRequest = $true
    $run = Join-Path $evidence 'run'
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.WorkingDirectory = $bin; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($arg in @('--audio-driver','Dummy','--rendering-method',$Renderer,'--resolution','1280x720','--','--qa-mode=soak',"--soak-output=$run","--soak-replays=$replays",("--soak-seconds=" + $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture)))) { $info.ArgumentList.Add($arg) }
    if ($Fast) { $info.ArgumentList.Add('--soak-fast') }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    if (!$process.Start()) { throw 'Could not start the isolated soak process.' }
    $started = $true; $clock.Start(); $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    $summary.processId = $process.Id; $summary.startedUtc = $process.StartTime.ToUniversalTime().ToString('o')
    [ordered]@{processId=$process.Id;startUtc=$summary.startedUtc;executable=$exe;coreModuleId=$coreModule;gameModuleId=$gameModule;requestedSeconds=$Seconds;fast=$Fast.IsPresent;renderer=$Renderer} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'process.json') -Encoding utf8
    $lastHeartbeat = 0.0; $lastElapsed = -1.0; $nextLog = 0.0
    while (!$process.WaitForExit(1000)) {
        $elapsed = $clock.Elapsed.TotalSeconds
        if ($elapsed -gt $Seconds + 600) { throw 'Soak exceeded its requested duration plus ten-minute completion/shutdown allowance.' }
        $heartbeatPath = Join-Path $run 'heartbeat.json'
        if (Test-Path -LiteralPath $heartbeatPath) {
            try {
                $heartbeat = Read-Heartbeat $heartbeatPath
                if ($heartbeat.processId -ne $process.Id) { throw 'Soak heartbeat came from a different process.' }
                if ($heartbeat.progress.ElapsedSeconds -gt $lastElapsed) { $lastElapsed = $heartbeat.progress.ElapsedSeconds; $lastHeartbeat = $elapsed }
                if ($elapsed -ge $nextLog) {
                    Write-Host ('Soak live: PID {0}, {1:F0}s, {2} ticks, {3} cycles, {4:F0} MiB RSS ({5})' -f $process.Id,$elapsed,$heartbeat.progress.SimulatedTicks,$heartbeat.cycles,($heartbeat.workingSetBytes/1MB),$heartbeat.phase)
                    $nextLog = $elapsed + 60
                }
            }
            catch [IO.IOException] { } # Atomic replacement can briefly change the path.
        }
        if ($elapsed - $lastHeartbeat -gt 45) { throw 'Live process has not advanced its heartbeat for 45 seconds.' }
    }
    $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $evidence 'engine.log'),$output)
    if ($process.ExitCode -ne 0 -or $output -match ('(?m)^(ERROR:|SCRIPT ERROR:)|' + $script:NativeLeakPattern)) { throw "Soak process failed or reported a native error/leak (exit $($process.ExitCode))." }
    $result = Get-Content -LiteralPath (Join-Path $run 'result.json') -Raw | ConvertFrom-Json
    $runtimeIdentity = Get-Content -LiteralPath (Join-Path $run 'identity.json') -Raw | ConvertFrom-Json
    if (!$result.passed -or $runtimeIdentity.processId -ne $process.Id -or $runtimeIdentity.coreModuleId -ne $coreModule -or $runtimeIdentity.gameModuleId -ne $gameModule) { throw 'Soak result or loaded runtime module identity differs from the isolated package.' }
    foreach ($file in $identity) { if ((Get-FileHash -LiteralPath (Join-Path $bin $file.path)).Hash -ne $file.sha256) { throw "Isolated package changed during soak: $($file.path)" } }
    if (@(Get-ChildItem -LiteralPath $bin -File -Recurse).Count -ne @($identity).Count) { throw 'Files were added to or removed from the isolated package during soak.' }
    $diagnostic = $Fast -or $Seconds -lt 7200
    if ($diagnostic -and $result.soakAcceptance) { throw 'Diagnostic run incorrectly claimed soak acceptance.' }
    if (!$diagnostic -and (!$result.soakAcceptance -or $clock.Elapsed.TotalSeconds -lt $Seconds -or $output -notmatch '(?m)^SOAK=PASS ')) { throw 'Actual two-hour native soak acceptance was not proved.' }
    $summary.wallClockSeconds = $clock.Elapsed.TotalSeconds; $summary.progress = $result.progress
    $summary.packageUnchanged = $true; $summary.runtimeModulesMatchPackage = $true; $summary.soakAcceptance = $result.soakAcceptance; $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    if ($soakPowerHeld) { $summary.keepAwakeRequestReleased = [Dungeons.Soaking.Power]::SetThreadExecutionState($soakPreviousExecutionState) -ne 0 }
    if ($process) {
        if ($started -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        if ($stdout -and $stderr) { [IO.File]::WriteAllText((Join-Path $evidence 'engine.log'),($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())) }
        $process.Dispose()
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Write-Host "Desktop soak evidence: $evidence (passed: $($summary.passed), two-hour acceptance: $($summary.soakAcceptance))"
}
