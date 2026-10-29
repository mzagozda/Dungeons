[CmdletBinding()]
param([string]$GodotPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
Assert-GodotVersion -Path $godot
$evidence = Join-Path $script:RepoRoot ('artifacts/m9/palette-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
Push-Location $script:RepoRoot
try {
    Invoke-Checked dotnet @('build','game/Dungeons.Game.csproj','-c','Debug','--no-restore','-warnaserror')
    Invoke-Checked $godot @('--audio-driver','Dummy','--path','game','--resolution','64x64','--minimized','res://editor/palette_icons.tscn','--','--write-editor-icons','--quality=High') -TimeoutSeconds 90 -RejectEngineErrors -LogPath (Join-Path $evidence 'capture.log') -Quiet
    [void](Invoke-GodotImport -Path $godot -LogPath (Join-Path $evidence 'import.log'))
    Invoke-Checked dotnet @('build','src/Dungeons.Tools','-c','Release','--no-restore','-warnaserror')
    Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','schema','--editor') -LogPath (Join-Path $evidence 'catalog.json') -Quiet
    Invoke-Checked python @('tools/check_editor_icons.py','--catalog',(Join-Path $evidence 'catalog.json'),'--contact-sheet',(Join-Path $evidence 'contact.png'))
    Write-Host "Original editor icons regenerated and checked: $evidence"
}
finally { Pop-Location }
