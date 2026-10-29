[CmdletBinding()]
param([string]$GodotPath, [switch]$Rendered)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
$evidence = Join-Path $script:RepoRoot ('artifacts/m4/gate-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
Push-Location $script:RepoRoot
try {
    # Includes the authoritative Core/coverage, content/import and Godot runtime gates.
    & (Join-Path $PSScriptRoot 'Test-M3.ps1') -GodotPath $godot -Rendered:$Rendered -CaptureSeconds 5
    if ($Rendered) {
        foreach ($action in @('roll', 'bow', 'melee', 'pickup')) {
            $png = Join-Path $evidence ($action + '.png')
            $log = Join-Path $evidence ($action + '.log')
            Invoke-Checked $godot @('--audio-driver','Dummy','--path','game','res://scenes/m4_preview.tscn','--',"--m4-action=$action","--m4-screenshot=$png") -TimeoutSeconds 45 -LogPath $log -RejectEngineErrors -Quiet
            if ([IO.File]::ReadAllText($log) -notmatch 'M4_PREVIEW=PASS' -or !(Test-Path -LiteralPath $png)) { throw "Missing M4 capture: $action" }
        }
    }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); localGatePassed=$Rendered.IsPresent; requiredPlatformChecksPending=$true; fullPlatformAcceptance=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "M4 checks passed. Evidence: $evidence"
}
finally { Pop-Location }
