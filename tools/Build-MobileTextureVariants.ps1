[CmdletBinding()]
param([string]$GodotPath, [Parameter(Mandatory)][string]$ProjectDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
Assert-GodotVersion $godot
$project = [IO.Path]::GetFullPath($ProjectDirectory)
Push-Location $script:RepoRoot
try {
    Invoke-Checked python @('tools/mobile_texture_variants.py','prepare',$project)
    Invoke-Checked $godot @('--headless','--path',$project,'--editor','--import') -TimeoutSeconds 300 -LogPath (Join-Path $project 'native-import.log') -RejectEngineErrors -Quiet
    Invoke-Checked $godot @('--headless','--path',$project,'--script',(Join-Path $PSScriptRoot 'audit_mobile_texture_variants.gd')) -TimeoutSeconds 60 -LogPath (Join-Path $project 'native-audit.log') -RejectEngineErrors -Quiet
    Invoke-Checked python @('tools/mobile_texture_variants.py','finish',$project)
    Write-Host "Native mobile texture variants: $project"
}
finally { Pop-Location }
