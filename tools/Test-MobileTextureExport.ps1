#requires -Version 7.0
[CmdletBinding()]
param([string]$GodotPath, [Parameter(Mandatory)][string]$ProfileDirectory, [string]$EvidenceDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
$profile = (Resolve-Path -LiteralPath $ProfileDirectory).Path
$original = [IO.File]::ReadAllText((Join-Path $profile 'manifest.json'))
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/mobile-textures/rejected-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Texture export rejection evidence must be new.' }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
[void][IO.Directory]::CreateDirectory($evidence)
$previous = $env:DUNGEONS_MOBILE_TEXTURE_DIR
$results = @()
Push-Location $script:RepoRoot
try {
    foreach ($case in @('missing-profile','changed-source','changed-data','missing-texture')) {
        $folder = Join-Path $evidence $case
        [void][IO.Directory]::CreateDirectory($folder)
        $manifest = $original | ConvertFrom-Json
        if ($case -eq 'missing-profile') {
            $env:DUNGEONS_MOBILE_TEXTURE_DIR = ''
            $expected = 'Native mobile texture variants are missing'
        }
        else {
            foreach ($entry in $manifest.textures) {
                foreach ($relative in @($entry.mobileImportFile,$entry.mobileDataFile)) {
                    $source = [IO.Path]::GetFullPath((Join-Path $profile $relative))
                    $target = [IO.Path]::GetFullPath((Join-Path $folder $relative))
                    if (!$source.StartsWith($profile + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !$target.StartsWith($folder + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Texture fixture path escapes its profile.' }
                    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
                    Copy-Item -LiteralPath $source -Destination $target
                }
            }
            switch ($case) {
                'changed-source' { $manifest.textures[0].desktopImportSha256 = '0' * 64; $expected = 'Desktop texture input changed or is invalid' }
                'changed-data' { [IO.File]::WriteAllBytes((Join-Path $folder $manifest.textures[0].mobileDataFile), [byte[]]@(1,2,3)); $expected = 'Native mobile texture file changed or escaped' }
                'missing-texture' { $manifest.textures = @($manifest.textures | Select-Object -Skip 1); $expected = 'Mobile texture manifest does not cover' }
            }
            [IO.File]::WriteAllText((Join-Path $folder 'manifest.json'),($manifest | ConvertTo-Json -Depth 10))
            $env:DUNGEONS_MOBILE_TEXTURE_DIR = $folder
        }
        $start = [Diagnostics.ProcessStartInfo]::new($godot)
        $start.WorkingDirectory=$script:RepoRoot; $start.UseShellExecute=$false; $start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
        foreach ($argument in @('--headless','--path','game','--export-pack','Android',(Join-Path $folder 'rejected.pck'))) { $start.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::new(); $process.StartInfo=$start
        try {
            [void]$process.Start(); $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
            $timedOut = !$process.WaitForExit(120000)
            if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
            $text=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()
            [IO.File]::WriteAllText((Join-Path $folder 'export.log'),$text)
            $unexpectedErrors = @([regex]::Matches($text,'(?m)^ERROR:.*$') | ForEach-Object Value | Where-Object { !$_.Contains($expected) -and !$_.Contains('Mobile ETC2 export did not include its complete audited texture set') })
            if ($timedOut -or !$text.Contains($expected) -or $text.Contains('MOBILE_TEXTURE_EXPORT=PASS') -or $text.Contains('_EDITOR_GET') -or $text.Contains('SCRIPT ERROR:') -or $unexpectedErrors.Count) { throw "Native texture export did not reject the expected boundary: $case" }
            $results += [ordered]@{case=$case; rejected=$true; processExit=$process.ExitCode; expectedDiagnostic=$expected}
            Write-Host "Mobile export rejected $case as expected."
        }
        finally { $process.Dispose() }
    }
    if ([IO.File]::ReadAllText((Join-Path $profile 'manifest.json')) -cne $original) { throw 'Original native profile was modified by rejection checks.' }
    [ordered]@{passed=$true; cases=$results; sourceProfilePreserved=$true; physicalAcceptance=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
}
finally { $env:DUNGEONS_MOBILE_TEXTURE_DIR=$previous; Pop-Location }
