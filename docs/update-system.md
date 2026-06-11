# Güncelleme Sistemi

Manifest her dosya için `path`, `size`, `sha256`, `version`, `downloadUrl`, `isRequired` ve `delete` alanlarını içerir.

## Akış

1. Uzak manifest indirilir.
2. Sürüm mevcut sürümle karşılaştırılır.
3. `UpdatePlanner` yerel SHA-256 değerlerini kontrol eder.
4. Yalnızca değişen dosyalar staging'e indirilir.
5. Updater oyun işleminin kapanmasını bekler.
6. Hedef dosyalar yedeklenir ve staging uygulanır.
7. Her hedef yeniden doğrulanır.
8. Hata halinde değişiklikler ters sırada geri alınır.

Yollar uygulama veya staging kökü dışına çıkamaz. Zip kullanılmadığından zip slip yüzeyi yoktur.

## Patch Oluşturma

```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
& $dotnet run --project tools/sample-update-generator -- release 1.0.1 https://github.com/OWNER/REPO/releases/download/v1.0.1
```

Oluşan `manifest.json` ve dosyalar GitHub Release asset olarak yüklenir. Repo bağlantısı kurulmadan release yayımlanmaz.
