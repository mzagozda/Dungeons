[CmdletBinding()]
param([string]$EvidenceDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/m9/thumbnail-' + [guid]::NewGuid().ToString('N')) }
[void][IO.Directory]::CreateDirectory($EvidenceDirectory)
$evidence = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
Push-Location $script:RepoRoot
try {
    $source = Join-Path $evidence 'level with spaces – test'
    [void][IO.Directory]::CreateDirectory($source)
    Copy-Item -LiteralPath 'game/levels/mossy_hollow/level.json' -Destination (Join-Path $source 'level.json')
    $content = Join-Path $evidence 'custom content'
    Copy-Item -LiteralPath 'game/content' -Destination $content -Recurse
    $raw = Join-Path $evidence 'raw.png'; $packed = Join-Path $evidence 'packed.png'; $package = Join-Path $evidence 'level.dlvl'
    Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','preview',(Join-Path $source 'level.json'),'--png',$raw,'--content',$content) -LogPath (Join-Path $evidence 'raw.log') -Quiet
    Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','pack',$source,$package) -LogPath (Join-Path $evidence 'pack.log') -Quiet
    Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','preview','--png',$package,$packed) -LogPath (Join-Path $evidence 'packed.log') -Quiet
    $before = (Get-FileHash -LiteralPath $raw -Algorithm SHA256).Hash
    $rejected = & dotnet run --project src/Dungeons.Tools -c Release --no-build -- preview $package --png $raw 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence 'overwrite-rejected.log'), $rejected)
    if ($exitCode -ne 1 -or $rejected -notmatch 'destination already exists' -or (Get-FileHash -LiteralPath $raw -Algorithm SHA256).Hash -ne $before) { throw 'Existing thumbnail was not preserved.' }
    $invalid = Join-Path $evidence 'invalid.json'; [IO.File]::WriteAllText($invalid, 'null')
    $invalidPng = Join-Path $evidence 'invalid.png'
    $rejected = & dotnet run --project src/Dungeons.Tools -c Release --no-build -- preview $invalid --png $invalidPng 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $evidence 'invalid-rejected.log'), $rejected)
    if ($exitCode -ne 1 -or (Test-Path -LiteralPath $invalidPng)) { throw 'Invalid document produced a thumbnail.' }
    foreach ($png in @($raw, $packed)) {
        Invoke-Checked python @('-c','from PIL import Image; import sys; im=Image.open(sys.argv[1]); assert im.size==(320,180); assert len(im.getcolors(320*180))>32; print(im.size,im.format)', $png)
    }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); rawAndPackagedPassed=$true; customContentAndUnicodePathsPassed=$true; existingOutputPreserved=$true; invalidInputRejected=$true; renderer='mobile'; physicalAcceptance=$false } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "Real CLI thumbnail checks passed: $evidence"
}
finally { Pop-Location }
