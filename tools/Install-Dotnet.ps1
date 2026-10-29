[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$sdkRepo = Split-Path -Parent $PSScriptRoot
$sdkVersion = (Get-Content -LiteralPath (Join-Path $sdkRepo 'global.json') -Raw | ConvertFrom-Json).sdk.version
$sdkPins = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dotnet-sdk.json') -Raw | ConvertFrom-Json
if ($sdkVersion -ne $sdkPins.version) { throw 'The SDK manifest must match global.json.' }
$sdkWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if ($sdkWindows) { $sdkRid = 'win-x64' }
elseif ($IsMacOS -and [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -eq 'Arm64') { $sdkRid = 'osx-arm64' }
elseif ($IsMacOS) { $sdkRid = 'osx-x64' }
else { $sdkRid = 'linux-x64' }
$sdkSource = $sdkPins.files.$sdkRid
$sdkExtension = $sdkSource.extension
$sdkHash = $sdkSource.sha512
if ($sdkHash -notmatch '^[0-9a-f]{128}$' -or $sdkSource.url -ne "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-$sdkRid.$sdkExtension") { throw 'Invalid official SDK archive pin.' }
$sdkTarget = Join-Path $sdkRepo '.toolchain/dotnet'
$sdkArchive = Join-Path $sdkRepo ".toolchain/dotnet-sdk-$sdkVersion-$sdkRid.$sdkExtension"
[void][IO.Directory]::CreateDirectory($sdkTarget)
if (!(Test-Path -LiteralPath $sdkArchive)) {
    $sdkCurl = if ($sdkWindows) { 'curl.exe' } else { 'curl' }
    & $sdkCurl -fL --retry 3 --output $sdkArchive $sdkSource.url
    if ($LASTEXITCODE -ne 0) { throw 'SDK download failed.' }
}
if ((Get-FileHash -LiteralPath $sdkArchive -Algorithm SHA512).Hash.ToLowerInvariant() -ne $sdkHash) { throw 'SDK SHA512 differs from the pinned official release metadata.' }
if ($sdkWindows) { Expand-Archive -LiteralPath $sdkArchive -DestinationPath $sdkTarget -Force }
else { & tar -xzf $sdkArchive -C $sdkTarget; if ($LASTEXITCODE -ne 0) { throw 'SDK extraction failed.' } }
$sdkExecutable = Join-Path $sdkTarget $(if ($sdkWindows) { 'dotnet.exe' } else { 'dotnet' })
$sdkActual = (& $sdkExecutable --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sdkActual -ne $sdkVersion) { throw "SDK verification failed: $sdkActual" }
Write-Host "Installed workspace-local .NET SDK $sdkActual. Build/test/export scripts activate it automatically."
