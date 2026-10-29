#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceManifest,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$Executable,
    [string]$ReplayDirectory,
    [ValidateSet(30)][int]$Fps = 30
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$IsWindows) { throw 'This review profile requires the native Windows package.' }
if (!$Executable) { $Executable = Join-Path $script:RepoRoot 'artifacts/windows/BlockDungeons.exe' }
if (!$ReplayDirectory) { $ReplayDirectory = Join-Path $script:RepoRoot 'artifacts/m6/candidates' }
$sourceExe = (Resolve-Path -LiteralPath $Executable).Path
$replays = (Resolve-Path -LiteralPath $ReplayDirectory).Path
$source = (Resolve-Path -LiteralPath $SourceManifest).Path
$manifest = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$evidence.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $evidence)) { throw 'Review evidence must be a new directory under repository artifacts.' }
$sourceDirectory = [IO.Path]::GetDirectoryName($sourceExe)
if ($evidence.StartsWith($sourceDirectory + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Review output must be outside the source package.' }
function Assert-SourceManifest {
    if (!$manifest.files -or $manifest.files.Count -lt 1) { throw 'Source manifest is empty.' }
    $known = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot $file.path))
        if (!$path.StartsWith($script:RepoRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -ine $file.sha256) { throw "Runtime/source checkpoint differs: $($file.path)" }
        [void]$known.Add($file.path.Replace('\','/'))
    }
    $current = @(git -C $script:RepoRoot -c core.quotepath=false ls-files --cached --others --exclude-standard -- game src/Dungeons.Core)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate current runtime source.' }
    foreach ($file in $current) { if (!$known.Contains($file.Replace('\','/'))) { throw "Runtime source is absent from the supplied manifest: $file" } }
}
Assert-SourceManifest
if ((Get-PSDrive -Name ([IO.Path]::GetPathRoot($evidence).Substring(0,1))).Free -lt 6GB) { throw 'Review capture requires at least 6 GiB free for the preserved AVI and conversion.' }
& (Join-Path $PSScriptRoot 'Install-VideoTools.ps1')
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;reviewStatus='pending';reviewedGolden=$false;motionReviewApproved=$false;audioListeningApproved=$false;physicalAcceptance=$false;soakAcceptance=$false;offlineMovie=$true;fps=$Fps;renderer='mobile';sourceManifestSha256=(Get-FileHash $source).Hash;sourceFiles=$manifest.files.Count}
try {
    Copy-Item -LiteralPath $source -Destination (Join-Path $evidence 'source-files.json')
    $bin = Join-Path $evidence 'bin'; [void][IO.Directory]::CreateDirectory($bin)
    Copy-Item -LiteralPath $sourceExe -Destination (Join-Path $bin 'BlockDungeons.exe')
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'data_Dungeons.Game_windows_x86_64') -Destination $bin -Recurse
    $sealed = @(Get-ChildItem -LiteralPath $bin -File -Recurse | ForEach-Object { [ordered]@{path=[IO.Path]::GetRelativePath($bin,$_.FullName);sha256=(Get-FileHash $_.FullName).Hash} })
    $sealed | ConvertTo-Json | Set-Content (Join-Path $evidence 'package-files.json')
    $avi = Join-Path $evidence 'gameplay.avi'; $run = Join-Path $evidence 'run'
    # The existing desktop QA scene owns an isolated profile and exercises the
    # actual app through clear/ordinary attempt/ambush/death-respawn. No --fast:
    # recorded streams advance at their normal 60 Hz in movie time. This short
    # offline recording explicitly cannot satisfy a wall-clock soak gate.
    Invoke-Checked (Join-Path $bin 'BlockDungeons.exe') @('--audio-driver','Dummy','--rendering-method','mobile','--resolution','1280x720','--fixed-fps',"$Fps",'--disable-vsync','--write-movie',$avi,'--','--qa-mode=soak',"--soak-output=$run","--soak-replays=$replays",'--soak-seconds=10') -TimeoutSeconds 1800 -LogPath (Join-Path $evidence 'engine.log') -RejectEngineErrors -Quiet
    $result = Get-Content (Join-Path $run 'result.json') -Raw | ConvertFrom-Json
    $identity = Get-Content (Join-Path $run 'identity.json') -Raw | ConvertFrom-Json
    if (!$result.passed -or !$result.diagnostic -or $result.soakAcceptance -or $identity.acceleratedDiagnostic -or $identity.display -eq 'headless' -or $identity.renderer -ne 'mobile' -or @($identity.replays).Count -ne 3 -or $result.progress.FullClears -lt 1 -or $result.progress.Ambushes -lt 1 -or $result.progress.Respawns -lt 1 -or $result.progress.NormalAttempts -lt 1) { throw 'Incomplete or incorrectly labeled offline gameplay capture.' }
    $log = [IO.File]::ReadAllText((Join-Path $evidence 'engine.log'))
    foreach ($replay in $identity.replays) {
        if (!$log.Contains("REPLAY_PLAYBACK=PASS ticks=$($replay.TickCount) finalHash=$($replay.finalHash) ")) { throw 'Missing actual replay completion identity in the movie run.' }
    }
    $ffmpeg = Join-Path $script:RepoRoot '.toolchain/video/ffmpeg.exe'
    $mp4 = Join-Path $evidence 'gameplay.mp4'
    Invoke-Checked $ffmpeg @('-hide_banner','-nostdin','-n','-i',$avi,'-map','0:v:0','-map','0:a:0','-c:v','libx264','-preset','fast','-crf','18','-threads','2','-pix_fmt','yuv420p','-c:a','aac','-b:a','160k','-movflags','+faststart',$mp4) -TimeoutSeconds 1800 -LogPath (Join-Path $evidence 'conversion.log') -Quiet
    $dimensions = [regex]::Match([IO.File]::ReadAllText((Join-Path $evidence 'conversion.log')),'(?m)^\s*Stream #0:0: Video:[^\r\n]*?, (\d+)x(\d+)(?:\s|,)')
    if (!$dimensions.Success) { throw 'Recorded video dimensions are missing.' }
    $summary.width = [int]$dimensions.Groups[1].Value; $summary.height = [int]$dimensions.Groups[2].Value
    $progress = Join-Path $evidence 'decode-progress.txt'
    Invoke-Checked $ffmpeg @('-hide_banner','-nostdin','-v','error','-xerror','-i',$mp4,'-map','0:v:0','-map','0:a:0','-progress',$progress,'-f','null','-') -TimeoutSeconds 600 -LogPath (Join-Path $evidence 'decode.log') -Quiet
    $decoded = [IO.File]::ReadAllText($progress)
    $frames = [long]([regex]::Matches($decoded,'(?m)^frame=(\d+)')[-1].Groups[1].Value)
    $duration = [double]([regex]::Matches($decoded,'(?m)^out_time_us=(\d+)')[-1].Groups[1].Value) / 1000000
    $ticks = ($identity.replays | Measure-Object -Property TickCount -Sum).Sum
    if ($decoded -notmatch '(?m)^progress=end' -or $frames -lt [math]::Ceiling($ticks * $Fps / 60) -or $duration -lt $ticks / 60) { throw 'Decoded movie does not contain the complete replay duration.' }
    Invoke-Checked $ffmpeg @('-hide_banner','-nostdin','-i',$mp4,'-vn','-af','astats=metadata=0:reset=0','-f','null','-') -TimeoutSeconds 600 -LogPath (Join-Path $evidence 'audio-stats.log') -Quiet
    $levels = [regex]::Matches([IO.File]::ReadAllText((Join-Path $evidence 'audio-stats.log')),'RMS level dB:\s*(-?\d+(?:\.\d+)?)')
    if (!$levels.Count) { throw 'Recorded audio is silent or lacks a finite RMS measurement.' }
    if (@(Get-ChildItem -LiteralPath $bin -File -Recurse).Count -ne $sealed.Count) { throw 'Review package gained/lost files during capture.' }
    foreach ($file in $sealed) { if ((Get-FileHash (Join-Path $bin $file.path)).Hash -ne $file.sha256) { throw 'Review package changed during capture.' } }
    Assert-SourceManifest
    $summary.video = $mp4; $summary.videoSha256 = (Get-FileHash $mp4).Hash; $summary.aviSha256 = (Get-FileHash $avi).Hash
    $summary.decodedFrames = $frames; $summary.durationSeconds = $duration; $summary.audioNonSilent = $true
    $summary.identity = $identity; $summary.progress = $result.progress; $summary.packageUnchanged = $true; $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally { $summary | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'result.json') }
Write-Host "Offline gameplay review candidate recorded; human motion/audio/golden reviews remain pending: $evidence"
