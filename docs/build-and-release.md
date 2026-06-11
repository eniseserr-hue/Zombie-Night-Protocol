# Build Ve Release

## Release Build

```powershell
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
& $dotnet build ZombieNightProtocol.sln -c Release
& $dotnet test ZombieNightProtocol.sln -c Release
```

## Self-Contained Windows x64

```powershell
.\tools\publish.ps1 -Configuration Release -Runtime win-x64
```

Ana çıktı `release/ZombieNightProtocol.exe`, updater ise `release/ZombieNightProtocol.Updater.exe` olur. `content/`, `update-config.json` ve `manifest.json` aynı dağıtım ağacında tutulur.

## Sürüm Yükseltme

Semantic Versioning kullanılır. App ve updater proje dosyalarındaki `Version`, `AssemblyVersion` ve `FileVersion` değerleri güncellenir; ardından yeni manifest üretilir.

Tek dosya publish teknik olarak mümkün olsa da patch sistemi içerik ve updater dosyalarının ayrı kalmasını gerektirdiği için önerilmez.
