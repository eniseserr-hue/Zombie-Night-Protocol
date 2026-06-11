# Mimari

## Katmanlar

- `Core`: domain modelleri, koşullar, effect'ler, hikâye motoru, undo ve doğrulama.
- `Infrastructure`: JSON erişimi, atomic save, ayarlar, log, hash, GitHub manifest ve patch planı.
- `App`: WPF, MVVM, navigasyon, tema, typewriter, sayaç ve kullanıcı etkileşimi.
- `Updater`: staging içeriğini uygular, doğrular, hata halinde rollback yapar ve oyunu yeniden açar.

## Veri Akışı

`JSON içerik -> repository -> StoryEngine -> GameState -> ViewModel -> WPF`

Seçim önce koşullardan geçirilir. Normal seçimden önce tek bir `GameState` snapshot'ı alınır. Effect'ler uygulanır, sonraki sahneye geçilir ve otomatik kayıt atomic olarak yazılır.

## Navigasyon

`MainViewModel` tek pencere içindeki ekran durumunu yönetir. Oyun içi durum paneli ayrı sayfa açmaz; sağdan overlay olarak görünür.

## Save Sistemi

Üç yuva `%LocalAppData%` altında JSON olarak tutulur. Yazma önce `.tmp` dosyasına yapılır, sonra `File.Replace` ile ana dosya değiştirilir ve `.bak` korunur.

## Update Sistemi

Uzak manifest yerel dosya hashleriyle karşılaştırılır. Değişen dosyalar staging'e alınır. Updater hedefleri izin verilen kök altında doğrular, yedekler, kopyalar ve SHA-256 kontrolünden sonra staging'i temizler.
