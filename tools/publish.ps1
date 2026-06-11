param(
    [string]$Configuration = "Release",
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

$release = Join-Path $root "release"
$appOutput = $release
$updaterOutput = Join-Path $root "artifacts\updater-publish"
$rootPrefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
foreach ($target in @($release, $updaterOutput)) {
    $resolvedTarget = [IO.Path]::GetFullPath($target).TrimEnd('\') + '\'
    if (-not $resolvedTarget.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish hedefi çalışma alanının dışında: $resolvedTarget"
    }
}

if (Test-Path -LiteralPath $release) {
    Remove-Item -LiteralPath $release -Recurse -Force
}
if (Test-Path -LiteralPath $updaterOutput) {
    Remove-Item -LiteralPath $updaterOutput -Recurse -Force
}

& $dotnet publish (Join-Path $root "src\ZombieNightProtocol.App\ZombieNightProtocol.App.csproj") -c $Configuration -r $Runtime --self-contained true --no-restore -o $appOutput
if ($LASTEXITCODE -ne 0) {
    throw "Uygulama publish işlemi başarısız oldu."
}
& $dotnet publish (Join-Path $root "src\ZombieNightProtocol.Updater\ZombieNightProtocol.Updater.csproj") -c $Configuration -r $Runtime --self-contained true --no-restore -o $updaterOutput
if ($LASTEXITCODE -ne 0) {
    throw "Updater publish işlemi başarısız oldu."
}

Copy-Item -LiteralPath (Join-Path $updaterOutput "ZombieNightProtocol.Updater.exe") -Destination (Join-Path $appOutput "ZombieNightProtocol.Updater.exe")
Copy-Item -LiteralPath (Join-Path $root "content\updates\manifest.json") -Destination (Join-Path $appOutput "manifest.json")
Write-Host "Release hazır: $appOutput"
