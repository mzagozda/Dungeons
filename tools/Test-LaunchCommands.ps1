#requires -Version 7.0
[CmdletBinding()]
param([string]$GodotPath, [string]$Executable, [string]$EvidenceDirectory, [switch]$Rendered)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/m8/launch-' + [guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($EvidenceDirectory)
$evidence = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$command = if ($Executable) { (Resolve-Path -LiteralPath $Executable).Path } else { Get-GodotPath -ExplicitPath $GodotPath }
[string[]]$projectArguments = @()
if (!$Executable) { $projectArguments = @('--path', (Join-Path $script:RepoRoot 'game')) }
$results = [Collections.Generic.List[object]]::new()
function Check-Launch([string]$Name, [string[]]$Flags, [int]$ExitCode, [string]$Marker, [switch]$Native) {
    $info = [Diagnostics.ProcessStartInfo]::new($command)
    $info.WorkingDirectory = $script:RepoRoot; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    [string[]]$engine = if ($Native) { @('--audio-driver', 'Dummy', '--resolution', '1280x720') } else { @('--headless') }
    foreach ($argument in ($engine + $projectArguments + @('--fixed-fps', '10', '--') + $Flags)) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        [void]$process.Start(); $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = !$process.WaitForExit(120000)
        if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $evidence ($Name + '.log')), $output)
        if ($timedOut) { throw "Launch check timed out: $Name. See $evidence" }
        if ($process.ExitCode -ne $ExitCode -or $output -notmatch $Marker -or $output -match '(?m)^(ERROR:|SCRIPT ERROR:)') {
            throw "Launch check failed: $Name (expected exit $ExitCode; actual $($process.ExitCode)). See $evidence"
        }
        $results.Add([ordered]@{ name=$Name; exitCode=$process.ExitCode; marker=$Marker; native=$Native.IsPresent; elapsedSeconds=$timer.Elapsed.TotalSeconds })
    }
    finally { $process.Dispose() }
}
Push-Location $script:RepoRoot
try {
    $source = Join-Path $evidence 'Résumé Ω level'; [void][IO.Directory]::CreateDirectory($source)
    $json = Join-Path $source 'level.json'; Copy-Item -LiteralPath 'game/levels/mossy_hollow/level.json' -Destination $json
    $package = Join-Path $evidence 'Résumé Ω.dlvl'; $replay = Join-Path $evidence 'Résumé Ω.dreplay'
    Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','pack',$source,$package) -LogPath (Join-Path $evidence 'pack.log') -Quiet
    Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','replay','--record-idle',$json,'120',$replay) -LogPath (Join-Path $evidence 'record.log') -Quiet
    Check-Launch 'validate-raw' @('--headless-validate',$json) 0 'HEADLESS_VALIDATE=PASS errors=0'
    Check-Launch 'validate-directory' @("--headless-validate=$source") 0 'HEADLESS_VALIDATE=PASS errors=0'
    Check-Launch 'validate-package' @('--headless-validate',$package) 0 'HEADLESS_VALIDATE=PASS errors=0'
    Check-Launch 'validate-resource' @('--headless-validate=res://levels/mossy_hollow/level.json') 0 'HEADLESS_VALIDATE=PASS errors=0'
    $warningSource = Join-Path $evidence 'warning level'; [void][IO.Directory]::CreateDirectory((Join-Path $warningSource 'assets'))
    Copy-Item -LiteralPath $json -Destination (Join-Path $warningSource 'level.json')
    [IO.File]::WriteAllText((Join-Path $warningSource 'assets/retained.txt'), 'Reserved v1 asset fixture')
    Check-Launch 'validate-warning' @('--headless-validate',$warningSource) 0 'HEADLESS_VALIDATE=PASS errors=0 warnings=1'
    $invalid = Join-Path $evidence 'invalid.json'; [IO.File]::WriteAllText($invalid, 'null')
    Check-Launch 'validate-invalid' @('--headless-validate',$invalid) 1 'HEADLESS_VALIDATE=FAIL errors=[1-9]'
    Check-Launch 'validate-missing' @('--headless-validate',(Join-Path $evidence 'missing.json')) 2 'HEADLESS_VALIDATE=FAIL Cannot read'
    Check-Launch 'missing-value' @('--level') 2 'COMMAND_LINE=FAIL Missing value'
    Check-Launch 'invalid-seed' @('--level=core.mossy_hollow','--seed=-1') 2 'COMMAND_LINE=FAIL --seed requires'
    Check-Launch 'duplicate-argument' @('--level=a','--level=b') 2 'COMMAND_LINE=FAIL Duplicate argument'
    $badReplay = Join-Path $evidence 'bad.dreplay'; [IO.File]::WriteAllBytes($badReplay, [byte[]]@(0xff,0xfe,0x00))
    Check-Launch 'replay-invalid-utf8' @('--replay',$badReplay,'--replay-quit') 1 'REPLAY_PLAYBACK=FAIL Replay is not valid UTF-8'
    $oversize = Join-Path $evidence 'oversize.dreplay'
    $stream = [IO.File]::Create($oversize); try { $stream.SetLength(64 * 1024 * 1024 + 1) } finally { $stream.Dispose() }
    Check-Launch 'replay-oversize' @('--replay',$oversize,'--replay-quit') 1 'REPLAY_PLAYBACK=FAIL Input exceeds 64 MiB'
    $bad = [IO.File]::ReadAllText($replay) | ConvertFrom-Json; $bad.levelHash = '0' * 64
    [IO.File]::WriteAllText($badReplay, ($bad | ConvertTo-Json -Depth 40))
    Check-Launch 'replay-identity' @('--replay',$badReplay,'--replay-quit') 1 'REPLAY_PLAYBACK=FAIL'
    Check-Launch 'replay-seed-conflict' @('--replay',$replay,'--seed=99','--replay-quit') 1 'REPLAY_PLAYBACK=FAIL --seed differs'
    $recorded = [IO.File]::ReadAllText($replay) | ConvertFrom-Json
    if ($Executable) {
        foreach ($rejected in @(
            @{name='unknown';flags=@('--qa-mode=unknown')},
            @{name='script';flags=@('--qa-mode=outside.gd')},
            @{name='scene';flags=@('--qa-mode=res://scenes/main.tscn')},
            @{name='duplicate';flags=@('--qa-mode=palette','--qa-mode=ui')},
            @{name='missing';flags=@('--qa-mode')}
        )) { Check-Launch ('qa-reject-' + $rejected.name) $rejected.flags 2 'PACKAGED_QA=FAIL Unknown or ambiguous diagnostic mode' }
    }
    Check-Launch 'replay-idle' @('--replay',$replay,'--replay-quit') 0 "REPLAY_PLAYBACK=PASS ticks=120 finalHash=$($recorded.finalHash) state=Running"
    foreach ($repeat in 1..2) { Check-Launch "replay-idle-drain-$repeat" @('--replay',$replay,'--replay-quit') 0 "REPLAY_PLAYBACK=PASS ticks=120 finalHash=$($recorded.finalHash) state=Running" }
    $fullClear = Join-Path $script:RepoRoot 'artifacts/m6/mossy-hollow-scripted.dreplay'
    if (!(Test-Path -LiteralPath $fullClear)) { throw 'Run tools/Test-Core.ps1 first to generate the full-clear development replay.' }
    $recorded = [IO.File]::ReadAllText($fullClear) | ConvertFrom-Json
    Check-Launch 'replay-full-clear' @('--replay',$fullClear,'--replay-quit') 0 "REPLAY_PLAYBACK=PASS ticks=$($recorded.tickCount) finalHash=$($recorded.finalHash) state=Won"
    if ($Rendered) {
        foreach ($mode in @('id','raw','package','replay')) {
            $png = Join-Path $evidence ($mode + '.png'); $flags = @("--screenshot-test=$png")
            $flags += switch ($mode) { 'id' { @('--level=core.mossy_hollow','--seed=42') } 'raw' { @('--level',$json,'--seed','42') } 'package' { @('--level',$package,'--seed=42') } 'replay' { @('--level',$package,'--replay',$fullClear,'--replay-quit') } }
            Check-Launch "native-$mode" $flags 0 'APP_SCREENSHOT=PASS chunks=6' -Native
            if (!(Test-Path -LiteralPath $png)) { throw "Missing capture: $png" }
            if ($mode -eq 'replay' -and [IO.File]::ReadAllText((Join-Path $evidence 'native-replay.log')) -notmatch "REPLAY_PLAYBACK=PASS ticks=$($recorded.tickCount) finalHash=$($recorded.finalHash) state=Won") { throw 'Native replay did not reach the recorded win.' }
        }
    }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); packaged=[bool]$Executable; rendered=$Rendered.IsPresent; passed=$true; checks=$results; reviewedGolden=$false; physicalAcceptance=$false } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "Launch command process checks passed ($($results.Count)): $evidence"
}
finally { Pop-Location }
