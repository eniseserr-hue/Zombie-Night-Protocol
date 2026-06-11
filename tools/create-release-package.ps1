param(
    [string]$Version = "1.0.1",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$zipPath = Join-Path $artifacts "ZombieNightProtocol-$Version-$Runtime.zip"
$manifestPath = Join-Path $root "update-manifest.json"
$notesPath = Join-Path $artifacts "release-notes-v$Version.md"

& (Join-Path $PSScriptRoot "publish.ps1") -Configuration Release -Runtime $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "Publish başarısız."
}

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$publishedManifest = Join-Path $root "release\update-manifest.json"
if (Test-Path -LiteralPath $publishedManifest) {
    Remove-Item -LiteralPath $publishedManifest -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $root "release\*") -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item -LiteralPath $zipPath).Length
$publishedAt = [DateTimeOffset]::Now.ToString("o")
$manifest = [ordered]@{
    version = $Version
    mandatory = $true
    releaseNotes = @(
        "Bölüm sonu sinematik ekranı ve sesli anlatım düzenlendi."
        "Ekip mesajları, görevler ve ayarlar iyileştirildi."
        "ZIP tabanlı güvenli güncelleme sistemi eklendi."
    )
    downloadUrl = "https://github.com/eniseserr-hue/Zombie-Night-Protocol/releases/download/v$Version/ZombieNightProtocol-$Version-$Runtime.zip"
    sha256 = $hash
    packageSize = $size
    publishedAt = $publishedAt
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Copy-Item -LiteralPath $manifestPath -Destination $publishedManifest -Force

@"
# Zombie Night Protocol v$Version

## Değişiklikler
- Bölüm sonuna özel sinematik ekran ve kayan jenerik
- Ekip üyeliğine göre mesaj filtreleme ve tekrar engeli
- Sahne ilerlemesine bağlı görev paneli
- Ayar ve sosyal bağlantı iyileştirmeleri
- SHA-256 doğrulamalı ZIP güncelleme ve rollback

Paket: ZombieNightProtocol-$Version-$Runtime.zip
SHA-256: $hash
Boyut: $size bayt
"@ | Set-Content -LiteralPath $notesPath -Encoding utf8

Write-Host "ZIP: $zipPath"
Write-Host "SHA-256: $hash"
Write-Host "Boyut: $size"
