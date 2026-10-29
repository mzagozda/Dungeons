#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$ReplayFile,
    [switch]$Native
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$ReplayFile) { $ReplayFile = Join-Path $script:RepoRoot 'artifacts/m6/candidates/mossy_hollow_full_clear.dreplay' }
if (!(Test-Path -LiteralPath $ReplayFile -PathType Leaf)) { throw 'Run Test-Core.ps1 first, or supply the actual recorded full-clear -ReplayFile.' }
$replay = (Resolve-Path -LiteralPath $ReplayFile).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'AOT compatibility evidence must use a new directory.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;shippingGameAnalysisPassed=$false;reflectionDisabledChecksPassed=$false;nativeBinaryChecksPassed=$false;debugTestGraphRestored=$false;nativeRequested=$Native.IsPresent;physicalAcceptance=$false;iosLinkAndDeviceAcceptance=$false;coreTargets=@('net8.0','netstandard2.1')}
$restoreFailure = $null
Push-Location $script:RepoRoot
try {
    $generated = Join-Path $evidence 'generated-game'
    Invoke-Checked dotnet @('build','game/Dungeons.Game.csproj','-t:Rebuild','-c','Release','-p:DungeonsIncludeEditor=false','-p:EnableAotAnalyzer=true','-p:EnableTrimAnalyzer=true','-p:EmitCompilerGeneratedFiles=true',('-p:CompilerGeneratedFilesOutputPath=' + $generated),'-warnaserror') -LogPath (Join-Path $evidence 'game-analysis.log') -Quiet
    $summary.shippingGameAnalysisPassed = $true
    $project = Join-Path $PSScriptRoot 'AotChecks/Dungeons.AotChecks.csproj'
    $run = Join-Path $evidence 'jit'
    Invoke-Checked dotnet @('run','--project',$project,'-c','Release','--',(Join-Path $script:RepoRoot 'game/content'),$replay,$run) -LogPath (Join-Path $evidence 'jit.log') -Quiet
    $result = Get-Content -LiteralPath (Join-Path $run 'result.json') -Raw | ConvertFrom-Json
    if (!$result.passed -or $result.reflectionEnabled -or $result.nativeAotBinary -or $result.verifiedReplayTicks -ne 11591 -or $result.verifiedReplayHash -ne 'a6da84d1de933f5c') { throw 'The reflection-disabled full-clear/persistence contract did not pass.' }
    $summary.reflectionDisabledChecksPassed = $true
    if ($Native) {
        $rid = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
        $binaryDirectory = Join-Path $evidence 'native-bin'
        # AotChecksNative is interpreted only by the executable project. Passing
        # PublishAot globally incorrectly asks the retained netstandard2.1 target
        # to produce native code during restore (NETSDK1207).
        Invoke-Checked dotnet @('publish',$project,'-c','Release','-r',$rid,'-p:AotChecksNative=true','--self-contained','-o',$binaryDirectory) -TimeoutSeconds 600 -LogPath (Join-Path $evidence 'native-publish.log') -Quiet
        $executable = Join-Path $binaryDirectory $(if ($IsWindows) { 'Dungeons.AotChecks.exe' } else { 'Dungeons.AotChecks' })
        $nativeRun = Join-Path $evidence 'native'
        Invoke-Checked $executable @((Join-Path $script:RepoRoot 'game/content'),$replay,$nativeRun) -LogPath (Join-Path $evidence 'native.log') -Quiet
        $nativeResult = Get-Content -LiteralPath (Join-Path $nativeRun 'result.json') -Raw | ConvertFrom-Json
        if (!$nativeResult.passed -or $nativeResult.reflectionEnabled -or !$nativeResult.nativeAotBinary -or $nativeResult.verifiedReplayTicks -ne 11591 -or $nativeResult.verifiedReplayHash -ne 'a6da84d1de933f5c') { throw 'The actual native executable did not verify the original full-clear identity.' }
        foreach ($relative in @('settings/settings.json','profile/profile.json','replay.dreplay','catalogue.json')) {
            if ((Get-FileHash -LiteralPath (Join-Path $run $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $nativeRun $relative)).Hash) { throw "JIT/native JSON bytes differ: $relative" }
        }
        $summary.nativeBinaryChecksPassed = $true; $summary.runtimeIdentifier = $rid; $summary.nativeExecutableSha256 = (Get-FileHash -LiteralPath $executable).Hash
    }
    $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    try {
        # Release-only shipping analysis changes NuGet's shared Game assets file.
        # Restore the locked Debug graph so subsequent --no-build test tasks still
        # import Microsoft.NET.Test.Sdk instead of exiting without running tests.
        Invoke-Checked dotnet @('restore','game/Dungeons.Game.csproj','--locked-mode','-p:Configuration=Debug','-p:DungeonsIncludeEditor=true') -LogPath (Join-Path $evidence 'debug-restore.log') -Quiet
        $testProject = (& dotnet msbuild game/Dungeons.Game.csproj -p:Configuration=Debug -p:DungeonsIncludeEditor=true -getProperty:IsTestProject | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $testProject -ine 'true') { throw 'The restored Game project is not evaluated as a test project.' }
        $summary.debugTestGraphRestored = $true
    }
    catch { $restoreFailure = $_.Exception.Message; $summary.debugRestoreFailure = $restoreFailure; $summary.passed = $false }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Pop-Location
}
if ($restoreFailure) { throw "AOT checker could not restore the Debug test graph: $restoreFailure" }
Write-Host "AOT compatibility checks passed (native=$($Native.IsPresent)); iOS link/device acceptance remains separate: $evidence"
