[CmdletBinding()]
param([string]$GodotPath, [switch]$Rendered, [switch]$Benchmarks)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
$evidence = Join-Path $script:RepoRoot ('artifacts/m7/gate-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
Push-Location $script:RepoRoot
try {
    & (Join-Path $PSScriptRoot 'Test-M6.ps1') -GodotPath $godot -Rendered:$Rendered -Benchmarks:$Benchmarks
    if ($Rendered) {
        foreach ($capture in @('phone', 'aim', 'left', 'tablet', 'low-mobile', 'low-compatibility')) {
            $mode = if ($capture.StartsWith('low-')) { 'aim' } else { $capture }
            $resolution = if ($mode -eq 'tablet') { '1280x960' } else { '1760x800' }
            $png = Join-Path $evidence ($capture + '.png'); $log = Join-Path $evidence ($capture + '.log')
            $engineArgs = @('--audio-driver', 'Dummy', '--path', 'game', '--resolution', $resolution)
            if ($capture -eq 'low-compatibility') { $engineArgs += @('--rendering-method', 'gl_compatibility') }
            $engineArgs += @('res://scenes/m7_preview.tscn', '--', "--m7-mode=$mode", "--m7-screenshot=$png")
            if ($mode -ne 'phone') { $engineArgs += '--m7-input-interference' }
            if ($capture.StartsWith('low-')) { $engineArgs += '--quality=Low' }
            Invoke-Checked $godot $engineArgs -TimeoutSeconds 45 -LogPath $log -RejectEngineErrors -Quiet
            if ([IO.File]::ReadAllText($log) -notmatch 'M7_PREVIEW=PASS' -or !(Test-Path -LiteralPath $png)) { throw "Missing M7 capture: $capture" }
        }
        & (Join-Path $PSScriptRoot 'Test-GraphicsRelaunch.ps1') -GodotPath $godot -EvidenceDirectory $evidence
    }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); rendered=$Rendered.IsPresent; localTouchAndGraphicsChecksPassed=$true; requiredDeviceChecksPending=$true; mobileMilestoneComplete=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "M7 local touch/graphics checks passed; device/performance/mobile feature acceptance remains pending. Evidence: $evidence"
}
finally { Pop-Location }
