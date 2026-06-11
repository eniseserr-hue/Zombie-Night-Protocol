# Audio

Müzik, ortam loop ve tek seferlik efektler bu klasörde tutulur. Eksik ses dosyası oyun akışını durdurmaz.
Audio paths are relative to the `content` directory.

- `audio.config.json` defines the uninterrupted loading/menu/character-selection music.
- Story scenes may define `ambientAudio`, `backgroundMusic`, and `oneShotSounds`.
- Sessiz sahnelerde sürekli ortam loop'u kullanılmaz; kısa olay efektleri sahne JSON'undan tetiklenir.
- `sfx/ui_click.wav` tüm oyun düğmelerinde kısa arayüz geri bildirimi sağlar.
- Music, ambience, effects, and master volume are controlled independently in settings.
