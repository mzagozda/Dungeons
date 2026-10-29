#requires -Version 7.0
[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$IsWindows) { throw 'The optional pinned review-video converter currently targets Windows x64.' }
$pin = Get-Content (Join-Path $PSScriptRoot 'video-toolchain.json') -Raw | ConvertFrom-Json
if ($pin.package -ne 'imageio-ffmpeg' -or $pin.version -ne '0.6.0' -or $pin.file -ne 'imageio_ffmpeg-0.6.0-py3-none-win_amd64.whl' -or $pin.sha256 -notmatch '^[a-f0-9]{64}$' -or ([uri]$pin.url).Scheme -ne 'https' -or ([uri]$pin.url).Host -ne 'files.pythonhosted.org' -or $pin.binaryEntry -ne 'imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe') { throw 'Invalid Windows QA video-tool pin.' }
$cache = Join-Path $script:RepoRoot '.toolchain/video'
[void][IO.Directory]::CreateDirectory($cache)
$wheel = Join-Path $cache $pin.file
if (!(Test-Path -LiteralPath $wheel)) {
    $curl = (Get-Command curl -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    Invoke-Checked $curl @('--fail','--location','--retry','3','--silent','--show-error','--output',$wheel,$pin.url)
}
if ((Get-FileHash $wheel).Hash -ine $pin.sha256) { throw 'Video-tool wheel SHA256 differs from its published pin.' }
$converter = Join-Path $cache 'ffmpeg.exe'
$zip = [IO.Compression.ZipFile]::OpenRead($wheel)
try {
    $entry = $zip.GetEntry($pin.binaryEntry)
    if (!$entry) { throw 'Pinned converter entry is missing.' }
    $inputStream = $entry.Open()
    try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($inputStream)) }
    finally { $inputStream.Dispose() }
    if (!(Test-Path -LiteralPath $converter)) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$converter,$false) }
    if ((Get-FileHash $converter).Hash -ne $expected) { throw 'Cached converter differs from the verified wheel.' }
}
finally { $zip.Dispose() }
Invoke-Checked $converter @('-version') -LogPath (Join-Path $cache 'version.txt') -Quiet
if ([IO.File]::ReadAllText((Join-Path $cache 'version.txt')) -notmatch '^ffmpeg version 7\.1(?:\s|-)') { throw 'Unexpected video converter version.' }
Write-Host "Verified optional Windows QA FFmpeg at $converter"
