[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'Common.ps1')
$bundlePin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bundletool.json') -Raw | ConvertFrom-Json
if ($bundlePin.version -notmatch '^\d+\.\d+\.\d+$' -or $bundlePin.file -ne "bundletool-all-$($bundlePin.version).jar" -or $bundlePin.sha256 -notmatch '^[a-f0-9]{64}$' -or $bundlePin.url -ne "https://github.com/google/bundletool/releases/download/$($bundlePin.version)/$($bundlePin.file)") { throw 'Invalid official bundletool pin.' }
$bundleCache = Join-Path $script:RepoRoot '.toolchain/bundletool'
[void][IO.Directory]::CreateDirectory($bundleCache)
$bundleJar = Join-Path $bundleCache $bundlePin.file
if (!(Test-Path -LiteralPath $bundleJar)) {
    $bundleCurl = (Get-Command curl -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    Invoke-Checked $bundleCurl @('--fail','--location','--retry','3','--silent','--show-error','--output',$bundleJar,$bundlePin.url)
}
if ((Get-FileHash -LiteralPath $bundleJar -Algorithm SHA256).Hash -ine $bundlePin.sha256) { throw 'bundletool SHA256 differs from the pinned official release asset digest.' }
$bundleJava = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME $(if ($IsWindows) { 'bin/java.exe' } else { 'bin/java' }) } else { (Get-Command java -ErrorAction Stop).Source }
$bundleActual = (& $bundleJava -jar $bundleJar version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $bundleActual -ne $bundlePin.version) { throw "Unexpected bundletool version: $bundleActual" }
Write-Host "Verified bundletool $bundleActual at $bundleJar"
