# Zombie Night Protocol

Zombie Night Protocol, WPF ve MVVM ile geliştirilmiş tek kişilik, seçim tabanlı bir Türkçe hayatta kalma oyunudur. Hikâye, karakterler, yerelleştirme ve güncelleme manifestleri uygulama kodundan bağımsız JSON dosyalarında tutulur.

## Gereksinimler

- Windows 10/11 x64
- Geliştirme için kararlı .NET 10 LTS SDK `10.0.300`
- Visual Studio 2026 veya `dotnet` CLI

`global.json` preview SDK kullanımını kapatır. Yayın çıktısı self-contained olduğu için oyuncunun ayrıca .NET kurması gerekmez.

## Çalıştırma

```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
& $dotnet run --project src/ZombieNightProtocol.App
```

Framework-dependent Debug `.exe` yalnızca sistemde global .NET Desktop Runtime varsa doğrudan açılır. Runtime kurmadan test etmek için:

```powershell
& $dotnet publish src/ZombieNightProtocol.App -c Debug -r win-x64 --self-contained true -o artifacts/ui-test
.\artifacts\ui-test\ZombieNightProtocol.exe
```

## Build Ve Test

```powershell
& $dotnet build ZombieNightProtocol.sln -c Release
& $dotnet test ZombieNightProtocol.sln -c Release
```

## Publish

```powershell
.\tools\publish.ps1
```

Self-contained Windows x64 çıktısı `release/` altında oluşur. İçerik ve updater ayrı dosyalar olarak kalır; bu nedenle ana oyun için tek dosya publish varsayılan değildir.

## GitHub Güncelleme Ayarı

Şu iki dosyada `OWNER_NAME` ve `REPOSITORY_NAME` değerlerini değiştir:

- `src/ZombieNightProtocol.App/update-config.json`
- `content/updates/manifest.json`

Public raw manifest veya GitHub Release asset URL'leri token gerektirmez. Uygulama ağ yoksa çevrimdışı devam eder; doğrulanmış zorunlu yeni sürüm bulunursa oyuna girişi engeller.

## İçerik Genişletme

- Hikâye: `content/stories/prototype-tr/story.json`
- Şema: `content/stories/story.schema.json`
- Karakterler: `content/characters/characters.tr.json`
- Yerelleştirme: `content/localization/resources.tr.json`

Yeni condition için `ConditionEvaluator`, yeni effect için `EffectProcessor` genişletilir. Bilinmeyen türler oyunu düşürmez ve loglanır.

## Kullanıcı Verileri

- Kayıtlar: `%LocalAppData%\ZombieNightProtocol\Saves`
- Ayarlar: `%LocalAppData%\ZombieNightProtocol\Settings`
- Loglar: `%LocalAppData%\ZombieNightProtocol\Logs`
- Update staging: `%LocalAppData%\ZombieNightProtocol\Updates\Staging`

## Bilinen Sınırlamalar

- Görsel ve sesler ilk prototipte stilize fallback arayüzü kullanır.
- GitHub güncelleme deposu yapılandırılmadıysa kontrol çevrimdışı moda düşer ve oyun açılmaya devam eder.
- Prototip tek bölüm ve 18 sahne içerir; içerik altyapısı dallanmayı destekler.
