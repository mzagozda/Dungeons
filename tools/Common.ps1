Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $script:RepoRoot '.toolchain/dotnet'
$localDotnetExecutable = Join-Path $localDotnet $(if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { 'dotnet.exe' } else { 'dotnet' })
if (Test-Path -LiteralPath $localDotnetExecutable) {
    $env:DOTNET_ROOT = $localDotnet
    if (!($env:PATH.Split([IO.Path]::PathSeparator) -contains $localDotnet)) { $env:PATH = $localDotnet + [IO.Path]::PathSeparator + $env:PATH }
}
$localPython = Join-Path $script:RepoRoot $(if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { '.toolchain/python/Scripts' } else { '.toolchain/python/bin' })
if ((Test-Path -LiteralPath (Join-Path $localPython 'python.exe')) -or (Test-Path -LiteralPath (Join-Path $localPython 'python'))) {
    if (!($env:PATH.Split([IO.Path]::PathSeparator) -contains $localPython)) { $env:PATH = $localPython + [IO.Path]::PathSeparator + $env:PATH }
}
$script:Toolchain = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'toolchain.json') -Raw | ConvertFrom-Json
$script:NativeLeakPattern = 'ObjectDB instances leaked|ObjectDB instance(?:s)? (?:was|were) leaked|RID(?:s| allocations)? [^\r\n]*leaked|resources still in use'

function Get-GodotPath {
    param([string]$ExplicitPath)
    if ($ExplicitPath) { $candidate = $ExplicitPath }
    elseif ($env:GODOT_BIN) { $candidate = $env:GODOT_BIN }
    else {
        $pathFile = Join-Path $script:RepoRoot '.toolchain/godot-path.txt'
        if (!(Test-Path -LiteralPath $pathFile)) { throw 'Run tools/Install-Godot.ps1 first or supply -GodotPath / GODOT_BIN.' }
        $candidate = (Get-Content -LiteralPath $pathFile -Raw).Trim()
    }
    # The Windows console wrapper waits for the editor AND all descendants.
    # Gradle daemons outlive exports, so automation must launch the editor directly.
    # This also migrates old cached paths and explicit console-wrapper arguments.
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT -and $candidate.EndsWith('_console.exe', [StringComparison]::OrdinalIgnoreCase)) {
        $editorPath = $candidate.Substring(0, $candidate.Length - '_console.exe'.Length) + '.exe'
        if (Test-Path -LiteralPath $editorPath) { $candidate = $editorPath }
    }
    return (Resolve-Path -LiteralPath $candidate).Path
}

function Assert-GodotVersion {
    param([string]$Path)
    $version = (& $Path --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or !$version.StartsWith("$($script:Toolchain.godotVersion).stable.mono.", [StringComparison]::Ordinal)) {
        throw "Expected Godot $($script:Toolchain.godotVersion) .NET, received: $version"
    }
    Write-Host "Godot: $version"
}

function Invoke-Checked {
    param([string]$FilePath, [string[]]$Arguments, [int]$TimeoutSeconds = 600, [string]$LogPath, [switch]$RejectEngineErrors, [switch]$Quiet)
    if ($Arguments | Where-Object { $_.Contains('"') }) { throw 'Double quotes are not supported in command arguments.' }
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = (@($Arguments | ForEach-Object { '"' + $_ + '"' })) -join ' '
    $startInfo.WorkingDirectory = (Get-Location).Path
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        $timedOut = $false
        while (!$process.WaitForExit(1000)) {
            if ($stopwatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $timedOut = $true
                $process.Kill()
                [void]$process.WaitForExit(5000)
                break
            }
        }
        $capturedOutput = ''
        if ($stdout.Wait(5000)) { $capturedOutput += $stdout.GetAwaiter().GetResult() }
        if ($stderr.Wait(5000)) { $capturedOutput += $stderr.GetAwaiter().GetResult() }
        if (!$Quiet) { Write-Host $capturedOutput }
        if ($LogPath) { [IO.File]::WriteAllText($LogPath, $capturedOutput) }
        if ($timedOut) { throw "$FilePath timed out after $TimeoutSeconds seconds. Artifacts alone do not prove export success." }
        if ($process.ExitCode -ne 0) { throw "$FilePath failed with exit code $($process.ExitCode)" }
        if ($RejectEngineErrors -and $capturedOutput -match '(?m)^(ERROR:|SCRIPT ERROR:)') { throw "$FilePath reported an engine error despite exit code zero. See $LogPath" }
        if ($RejectEngineErrors -and $capturedOutput -match $script:NativeLeakPattern) { throw "$FilePath reported a native shutdown leak. See $LogPath" }
    }
    finally { $process.Dispose() }
}

function Invoke-GodotImport {
    param([string]$Path, [string]$LogPath)
    Invoke-Checked $Path @('--headless', '--path', 'game', '--editor', '--import') -LogPath $LogPath -Quiet
    $importText = [IO.File]::ReadAllText($LogPath)
    # Only the exact pinned 4.4.1 glTF dummy-preview diagnostic is known/allowed.
    $known = '(?m)^ERROR: Parameter "t" is null\.\r?\n\s+at: texture_2d_get \(servers/rendering/dummy/storage/texture_storage.h:107\)\r?\n'
    $knownCount = if ($script:Toolchain.godotVersion -eq '4.4.1') { [regex]::Matches($importText, $known).Count } else { 0 }
    $unexpected = if ($knownCount) { [regex]::Replace($importText, $known, '') } else { $importText }
    if ($unexpected -match '(?m)^(ERROR:|SCRIPT ERROR:)') { throw "Unexpected Godot import error; see $LogPath" }
    if ($unexpected -match $script:NativeLeakPattern) { throw "Godot import reported a native shutdown leak; see $LogPath" }
    if ($knownCount) { Write-Warning "Pinned engine editor-preview issue #108994 occurred $knownCount times; raw errors retained in $LogPath" }
    return $knownCount
}

function Invoke-Probe {
    param([string]$Executable, [string[]]$Arguments, [string]$LogPath, [switch]$Packaged)
    # Bound all smoke runs, require the probe's own success marker, and capture diagnostics.
    if ($Arguments | Where-Object { $_.Contains('"') }) { throw 'Double quotes are not supported in probe arguments.' }
    $quotedArguments = @($Arguments | ForEach-Object { '"' + $_ + '"' })
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $Executable
    $startInfo.Arguments = $quotedArguments -join ' '
    $startInfo.WorkingDirectory = $script:RepoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(45000)) {
            $process.Kill()
            throw 'M0 smoke run timed out after 45 seconds.'
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($LogPath, $output)
        Write-Host $output
        if ($process.ExitCode -ne 0 -or $output -notmatch 'M0_PROBE=PASS' -or $output -match '(?m)^(ERROR:|SCRIPT ERROR:)') {
            throw "M0 probe failed; see $LogPath"
        }
        $reportMatch = [regex]::Match($output, '(?m)^M0_PROBE=PASS report=(.+)$')
        if (!$reportMatch.Success) { throw 'Probe did not identify its report file.' }
        $reportPath = $reportMatch.Groups[1].Value.Trim()
        $report = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
        if (!$report.passed -or $report.milestone -ne 'M0' -or $report.packaged -ne $Packaged.IsPresent) {
            throw 'Probe report does not match the expected source/exported run.'
        }
        Copy-Item -LiteralPath $reportPath -Destination ([IO.Path]::ChangeExtension($LogPath, '.json'))
    }
    finally { $process.Dispose() }
}

function Initialize-GodotEditor {
    param([string]$Path)
    # Generated publish/Gradle data must never trigger resource imports or get packed.
    foreach ($generatedDirectory in @('android', 'obj')) {
        $generatedPath = Join-Path $script:RepoRoot "game/$generatedDirectory"
        [void][IO.Directory]::CreateDirectory($generatedPath)
        [IO.File]::WriteAllText((Join-Path $generatedPath '.gdignore'), '')
    }
    # Set only these tooling paths, preserving all other user editor preferences.
    # Editing settings before launch avoids running an alternate SceneTree inside the editor.
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        $configDir = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Godot'
    }
    elseif (Test-Path -LiteralPath '/System/Library/CoreServices/SystemVersion.plist') {
        $configDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Library/Application Support/Godot'
    }
    elseif ($env:XDG_CONFIG_HOME) { $configDir = Join-Path $env:XDG_CONFIG_HOME 'godot' }
    else { $configDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.config/godot' }
    [void][IO.Directory]::CreateDirectory($configDir)
    $minorVersion = ($script:Toolchain.godotVersion -split '\.')[0..1] -join '.'
    $settingsFile = Join-Path $configDir "editor_settings-$minorVersion.tres"
    $settings = if (Test-Path -LiteralPath $settingsFile) { [IO.File]::ReadAllText($settingsFile) } else {
        "[gd_resource type=`"EditorSettings`" format=3]`n`n[resource]`n"
    }
    $values = @{}
    if ($env:JAVA_HOME) { $values['export/android/java_sdk_path'] = $env:JAVA_HOME }
    if ($env:ANDROID_HOME) { $values['export/android/android_sdk_path'] = $env:ANDROID_HOME }
    elseif ($env:ANDROID_SDK_ROOT) { $values['export/android/android_sdk_path'] = $env:ANDROID_SDK_ROOT }
    $resourceEditor = Join-Path $script:RepoRoot '.toolchain/rcedit-x64.exe'
    if (Test-Path -LiteralPath $resourceEditor) { $values['export/windows/rcedit'] = $resourceEditor }
    foreach ($key in $values.Keys) {
        $value = $values[$key].Replace('\', '/').Replace('"', '\"')
        $line = "$key = `"$value`""
        $pattern = '(?m)^' + [regex]::Escape($key) + '\s*=.*$'
        if ([regex]::IsMatch($settings, $pattern)) { $settings = [regex]::Replace($settings, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($match) $line }) }
        else { $settings += "`n$line`n" }
    }
    [IO.File]::WriteAllText($settingsFile, $settings, (New-Object System.Text.UTF8Encoding($false)))
}
