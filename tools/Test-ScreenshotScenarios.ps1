#requires -Version 7.0
[CmdletBinding()]
param([string]$GodotPath, [string]$Executable, [string]$EvidenceDirectory, [string[]]$Scenarios = @(), [ValidateSet('mobile','gl_compatibility')][string]$Renderer = 'mobile', [switch]$Repeat)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/m8/scenarios-' + [guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($EvidenceDirectory); $evidence = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$command = if ($Executable) { (Resolve-Path -LiteralPath $Executable).Path } else { Get-GodotPath -ExplicitPath $GodotPath }
[string[]]$project = @(); if (!$Executable) { $project = @('--path',(Join-Path $script:RepoRoot 'game')) }
function Check-Rejected([string]$Name, [string[]]$Flags, [int]$ExitCode, [string]$Marker, [switch]$Native) {
    $info = [Diagnostics.ProcessStartInfo]::new($command); $info.WorkingDirectory=$script:RepoRoot; $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    [string[]]$engine = if ($Native) { @('--audio-driver','Dummy','--rendering-method',$Renderer) } else { @('--headless') }
    foreach($argument in ($engine + $project + @('--') + $Flags)) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $info
    try {
        [void]$process.Start(); $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        $timedOut = !$process.WaitForExit(30000); if($timedOut) { $process.Kill($true); $process.WaitForExit() }
        $text=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult(); [IO.File]::WriteAllText((Join-Path $evidence ($Name+'.log')),$text)
        if($timedOut -or $process.ExitCode -ne $ExitCode -or $text -notmatch $Marker -or $text -match '(?m)^(ERROR:|SCRIPT ERROR:)') { throw "Negative screenshot check failed: $Name" }
    } finally { $process.Dispose() }
}
Push-Location $script:RepoRoot
try {
    $catalogLog = Join-Path $evidence 'catalog.log'
    Invoke-Checked $command (@('--headless') + $project + @('--','--screenshot-list')) -TimeoutSeconds 30 -RejectEngineErrors -LogPath $catalogLog -Quiet
    $match = [regex]::Match([IO.File]::ReadAllText($catalogLog), '(?m)^SCREENSHOT_SCENARIOS=(.+)$')
    if (!$match.Success) { throw 'Screenshot catalogue was not returned.' }
    $catalog = @($match.Groups[1].Value | ConvertFrom-Json)
    if (!$catalog.Count -or @($catalog.id | Select-Object -Unique).Count -ne $catalog.Count) { throw 'Screenshot catalogue is empty or ambiguous.' }
    foreach ($name in $Scenarios) { if ($name -notin $catalog.id) { throw "Unknown selected scenario: $name" } }
    $selected = @($catalog | Where-Object { !$Scenarios.Count -or $_.id -in $Scenarios })
    Check-Rejected 'unknown-scenario' @('--screenshot-test=unknown') 2 'COMMAND_LINE=FAIL Unknown screenshot scenario'
    Check-Rejected 'dummy-renderer' @('--screenshot-test=hero-roll') 2 'SCREENSHOT_SCENARIO=FAIL Named screenshot scenarios require'
    Check-Rejected 'missing-replay' @('--screenshot-test=mission-complete') 2 'COMMAND_LINE=FAIL This screenshot scenario requires --replay'
    Check-Rejected 'missing-legacy-level' @('--screenshot-test=out.png') 2 'COMMAND_LINE=FAIL PNG screenshot capture requires'
    $badOutput=Join-Path $evidence 'wrong-extension.jpg'
    Check-Rejected 'invalid-extension' @('--screenshot-test=hero-roll','--screenshot-output',$badOutput) 2 'SCREENSHOT_SCENARIO=FAIL Screenshot output must end with' -Native
    $reservedOutput=Join-Path $evidence 'reserved.png'; $companion=[IO.Path]::ChangeExtension($reservedOutput,'.capture.json'); [IO.File]::WriteAllText($companion,'preserved companion')
    Check-Rejected 'existing-companion' @('--screenshot-test=hero-roll','--screenshot-output',$reservedOutput) 1 'SCREENSHOT_SCENARIO=FAIL Screenshot output already exists' -Native
    if([IO.File]::ReadAllText($companion) -ne 'preserved companion' -or (Test-Path -LiteralPath $reservedOutput)) { throw 'Existing capture companion was modified.' }
    $captures = [Collections.Generic.List[object]]::new()
    foreach ($scenario in $selected) {
        $files = @(); $runs = if ($Repeat) { 2 } else { 1 }
        foreach ($run in 1..$runs) {
            $png = Join-Path $evidence ($scenario.id + '-' + $run + '.png'); $log = [IO.Path]::ChangeExtension($png,'.log')
            $arguments = @('--audio-driver','Dummy','--rendering-method',$Renderer,'--resolution','1280x720','--fixed-fps','60') + $project + @('--','--screenshot-test',$scenario.id,'--screenshot-output',$png)
            if ($scenario.needsReplay) { $arguments += @('--replay',(Join-Path $script:RepoRoot 'artifacts/m6/mossy-hollow-scripted.dreplay')) }
            Invoke-Checked $command $arguments -TimeoutSeconds 60 -RejectEngineErrors -LogPath $log -Quiet
            $text = [IO.File]::ReadAllText($log)
            if ($text -notmatch ('SCREENSHOT_SCENARIO=PASS name=' + [regex]::Escape($scenario.id) + ' ') -or !$text.Contains($scenario.expectedMarker) -or !(Test-Path -LiteralPath $png)) { throw "Missing scenario acceptance: $($scenario.id)" }
            $metadata = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($png,'.capture.json')) -Raw | ConvertFrom-Json
            if (!$metadata.passed -or $metadata.scenario -ne $scenario.id -or $metadata.renderer -ne $Renderer -or $metadata.baselineApproved -or $metadata.physicalAcceptance -or $metadata.shaderTime -ne 0 -or $metadata.imageSha256 -ne (Get-FileHash -LiteralPath $png -Algorithm SHA256).Hash.ToLowerInvariant()) { throw "Invalid capture provenance: $($scenario.id)" }
            if ($scenario.width -gt 0 -and ($metadata.width -ne $scenario.width -or $metadata.height -ne $scenario.height)) { throw "Scenario resolution differs: $($scenario.id)" }
            $files += $png; $captures.Add($metadata)
        }
        if ($Repeat) {
            Invoke-Checked python @('tools/compare_images.py',$files[0],$files[1],'--report',(Join-Path $evidence ($scenario.id + '-repeat.json')),'--diff',(Join-Path $evidence ($scenario.id + '-repeat.diff.png'))) -Quiet
            $first = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($files[0],'.capture.json')) -Raw | ConvertFrom-Json
            $second = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($files[1],'.capture.json')) -Raw | ConvertFrom-Json
            foreach ($key in @('levelHash','contentHash','seed','tick','finalHash','width','height','quality','adapter')) { if ($first.$key -ne $second.$key) { throw "Repeat changed $key for $($scenario.id)" } }
        }
    }
    $preserved=Join-Path $evidence ($selected[0].id+'-1.png'); $before=(Get-FileHash -LiteralPath $preserved -Algorithm SHA256).Hash
    Check-Rejected 'existing-output' @('--screenshot-test=hero-roll','--screenshot-output',$preserved) 1 'SCREENSHOT_SCENARIO=FAIL Screenshot output already exists' -Native
    if((Get-FileHash -LiteralPath $preserved -Algorithm SHA256).Hash -ne $before) { throw 'Existing screenshot was modified.' }
    [ordered]@{capturedUtc=[DateTime]::UtcNow.ToString('o'); passed=$true; renderer=$Renderer; packaged=[bool]$Executable; scenarios=$selected.Count; captures=$captures; repeatabilityChecked=$Repeat.IsPresent; baselineApproved=$false; visualRegressionAgainstReviewedBaselinePassed=$false; physicalAcceptance=$false} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "Named screenshot scenarios passed: $($selected.Count) ($Renderer; repeat=$Repeat). Evidence: $evidence"
}
finally { Pop-Location }
