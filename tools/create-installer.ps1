param(
    [string]$Version = "1.0.4",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
}
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

$output = Join-Path $root "artifacts\installer-publish"
$setupPath = Join-Path $root "artifacts\ZombieNightProtocol-Setup-$Version-$Runtime.exe"
if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $root "artifacts") -Force | Out-Null

& $dotnet publish (Join-Path $root "src\ZombieNightProtocol.Installer\ZombieNightProtocol.Installer.csproj") -c Release -r $Runtime --self-contained true --no-restore -o $output
if ($LASTEXITCODE -ne 0) {
    throw "Installer publish basarisiz."
}

Copy-Item -LiteralPath (Join-Path $output "ZombieNightProtocol.Setup.exe") -Destination $setupPath -Force
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $setupPath).Length

Write-Host "SETUP: $setupPath"
Write-Host "SHA-256: $hash"
Write-Host "Boyut: $size"
