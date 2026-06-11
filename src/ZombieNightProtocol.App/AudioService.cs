using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Windows.Media;
using ZombieNightProtocol.Core;

namespace ZombieNightProtocol.App;

public sealed class AudioService
{
    private readonly string _contentRoot;
    private readonly IGameDiagnostics _diagnostics;
    private readonly MediaPlayer _menuMusic = new();
    private readonly MediaPlayer _sceneMusic = new();
    private readonly MediaPlayer _ambient = new();
    private readonly MediaPlayer _voice = new();
    private readonly List<MediaPlayer> _oneShots = [];
    private readonly ConcurrentQueue<bool> _notificationQueue = new();
    private readonly HashSet<string> _missingAudioWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _notificationWorkerLock = new();
    private GameSettings _settings = new();
    private readonly AudioConfig _config;
    private string _menuPath = "";
    private string _sceneMusicPath = "";
    private string _ambientPath = "";
    private string _voicePath = "";
    private DateTimeOffset _lastUiClickAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastNotificationSoundAt = DateTimeOffset.MinValue;
    private bool _isNotificationWorkerRunning;
    private CancellationTokenSource? _transitionCancellation;

    public AudioService(string contentRoot, IGameDiagnostics? diagnostics = null)
    {
        _contentRoot = contentRoot;
        _diagnostics = diagnostics ?? new NullGameDiagnostics();
        _config = LoadAudioConfig();
        MenuMusicPath = _config.MenuMusic;
        _menuMusic.MediaEnded += (_, _) => Loop(_menuMusic);
        _sceneMusic.MediaEnded += (_, _) => Loop(_sceneMusic);
        _ambient.MediaEnded += (_, _) => Loop(_ambient);
        _voice.MediaFailed += (_, args) => _diagnostics.Warning($"Sesli kayıt oynatılamadı: {_voicePath} ({args.ErrorException.Message})");
    }

    public string MenuMusicPath { get; }

    public void ApplySettings(GameSettings settings)
    {
        _settings = settings;
        _menuMusic.Volume = MusicVolume;
        _sceneMusic.Volume = MusicVolume;
        _ambient.Volume = AmbientVolume;
        foreach (var player in _oneShots)
        {
            player.Volume = EffectsVolume;
        }
        _voice.Volume = VoiceVolume;
    }

    public void StartMenuMusic(string relativePath, bool fadeIn)
    {
        var path = Resolve(relativePath);
        if (path is null)
        {
            return;
        }

        _transitionCancellation?.Cancel();
        StopAndClose(_sceneMusic);
        StopAndClose(_ambient);

        if (!_menuPath.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            StopAndClose(_menuMusic);
            _menuMusic.Open(new Uri(path, UriKind.Absolute));
            _menuPath = path;
            _menuMusic.Volume = fadeIn ? 0 : MusicVolume;
            _menuMusic.Play();
        }
        else
        {
            _menuMusic.Play();
        }

        if (fadeIn)
        {
            _ = FadeAsync(_menuMusic, MusicVolume, TimeSpan.FromSeconds(3), closeAtEnd: false);
        }
        else
        {
            _menuMusic.Volume = MusicVolume;
        }
    }

    public void EnterScene(StoryScene scene)
    {
        _transitionCancellation?.Cancel();
        _transitionCancellation = new CancellationTokenSource();
        _ = TransitionToSceneAsync(scene, _transitionCancellation.Token);
    }

    private async Task TransitionToSceneAsync(StoryScene scene, CancellationToken token)
    {
        await FadeAsync(_menuMusic, 0, TimeSpan.FromSeconds(3), closeAtEnd: true, token);
        if (token.IsCancellationRequested)
        {
            return;
        }
        StartLoop(_sceneMusic, scene.BackgroundMusic, ref _sceneMusicPath, MusicVolume, token);
        StartLoop(_ambient, scene.AmbientAudio, ref _ambientPath, AmbientVolume, token);
        foreach (var sound in scene.OneShotSounds)
        {
            PlayEffect(sound);
        }
    }

    public void ReturnToMenu(string menuMusicPath)
    {
        _transitionCancellation?.Cancel();
        _transitionCancellation = new CancellationTokenSource();
        _ = FadeAsync(_sceneMusic, 0, TimeSpan.FromSeconds(1.2), closeAtEnd: true);
        _ = FadeAsync(_ambient, 0, TimeSpan.FromSeconds(1.2), closeAtEnd: true);
        _menuPath = "";
        _sceneMusicPath = "";
        _ambientPath = "";
        StartMenuMusic(menuMusicPath, fadeIn: true);
    }

    public void PlayEffect(string relativePath)
    {
        var path = Resolve(relativePath);
        if (path is null)
        {
            return;
        }

        var player = new MediaPlayer { Volume = EffectsVolume };
        player.MediaEnded += (_, _) => ReleaseOneShot(player);
        player.MediaFailed += (_, _) => ReleaseOneShot(player);
        _oneShots.Add(player);
        player.Open(new Uri(path, UriKind.Absolute));
        player.Play();
    }

    public void PlayNotification(bool danger)
    {
        if (!_settings.NotificationSounds)
        {
            return;
        }

        _notificationQueue.Enqueue(danger);
        EnsureNotificationWorker();
    }

    public void PlayUiClick()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastUiClickAt < TimeSpan.FromMilliseconds(75))
        {
            return;
        }

        _lastUiClickAt = now;
        PlayEffect(_config.UiClickSound, AudioChannel.UiClick);
    }

    public bool HasAudioFile(string relativePath) => Resolve(relativePath, warnIfMissing: false) is not null;

    public void PlayVoice(string relativePath, double volumeMultiplier = 1)
    {
        var path = Resolve(relativePath);
        if (path is null)
        {
            return;
        }

        if (!_voicePath.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            StopAndClose(_voice);
            _voice.Open(new Uri(path, UriKind.Absolute));
            _voicePath = path;
        }

        _voice.Volume = Math.Clamp(VoiceVolume * volumeMultiplier, 0, 1);
        _voice.Play();
    }

    public void PauseVoice() => _voice.Pause();

    public void RestartVoice()
    {
        _voice.Position = TimeSpan.Zero;
        _voice.Play();
    }

    public void StopVoice()
    {
        StopAndClose(_voice);
        _voicePath = "";
    }

    public TimeSpan VoicePosition => _voice.Position;
    public TimeSpan? VoiceDuration => _voice.NaturalDuration.HasTimeSpan ? _voice.NaturalDuration.TimeSpan : null;

    public void StopAll()
    {
        _transitionCancellation?.Cancel();
        StopAndClose(_menuMusic);
        StopAndClose(_sceneMusic);
        StopAndClose(_ambient);
        _menuPath = "";
        _sceneMusicPath = "";
        _ambientPath = "";
        foreach (var player in _oneShots.ToArray())
        {
            ReleaseOneShot(player);
        }
        while (_notificationQueue.TryDequeue(out _))
        {
        }
        StopVoice();
    }

    private void EnsureNotificationWorker()
    {
        lock (_notificationWorkerLock)
        {
            if (_isNotificationWorkerRunning)
            {
                return;
            }

            _isNotificationWorkerRunning = true;
            _ = ProcessNotificationQueueAsync();
        }
    }

    private async Task ProcessNotificationQueueAsync()
    {
        try
        {
            while (_notificationQueue.TryDequeue(out _))
            {
                if (!_settings.NotificationSounds)
                {
                    continue;
                }

                var elapsed = DateTimeOffset.UtcNow - _lastNotificationSoundAt;
                if (elapsed < TimeSpan.FromMilliseconds(900))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(900) - elapsed);
                }

                _lastNotificationSoundAt = DateTimeOffset.UtcNow;
                PlayEffect(_config.NotificationSound, AudioChannel.Notification);
                await Task.Delay(900);
            }
        }
        finally
        {
            lock (_notificationWorkerLock)
            {
                _isNotificationWorkerRunning = false;
                if (!_notificationQueue.IsEmpty)
                {
                    EnsureNotificationWorker();
                }
            }
        }
    }

    private void StartLoop(
        MediaPlayer player,
        string relativePath,
        ref string currentPath,
        double targetVolume,
        CancellationToken token)
    {
        var path = Resolve(relativePath);
        if (path is null)
        {
            StopAndClose(player);
            currentPath = "";
            return;
        }

        if (!currentPath.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            StopAndClose(player);
            player.Open(new Uri(path, UriKind.Absolute));
            currentPath = path;
        }
        player.Volume = 0;
        player.Play();
        _ = FadeAsync(player, targetVolume, TimeSpan.FromSeconds(2), closeAtEnd: false, token);
    }

    private async Task FadeAsync(
        MediaPlayer player,
        double target,
        TimeSpan duration,
        bool closeAtEnd,
        CancellationToken cancellationToken = default)
    {
        var start = player.Volume;
        const int steps = 30;
        try
        {
            for (var step = 1; step <= steps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                player.Volume = Math.Clamp(start + ((target - start) * step / steps), 0, 1);
                await Task.Delay(duration / steps, cancellationToken);
            }
            if (closeAtEnd && target <= 0)
            {
                StopAndClose(player);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void PlayEffect(string relativePath, AudioChannel channel)
    {
        var path = Resolve(relativePath);
        if (path is null)
        {
            return;
        }

        var player = new MediaPlayer { Volume = VolumeFor(channel) };
        player.MediaEnded += (_, _) => ReleaseOneShot(player);
        player.MediaFailed += (_, _) => ReleaseOneShot(player);
        _oneShots.Add(player);
        player.Open(new Uri(path, UriKind.Absolute));
        player.Play();
    }

    private string? Resolve(string relativePath, bool warnIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(_contentRoot, normalized));
        var root = Path.GetFullPath(_contentRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            return path;
        }

        if (warnIfMissing && _missingAudioWarnings.Add(relativePath))
        {
            _diagnostics.Warning($"Ses dosyası bulunamadı: {relativePath}");
        }
        return null;
    }

    private AudioConfig LoadAudioConfig()
    {
        var path = Path.Combine(_contentRoot, "audio", "audio.config.json");
        if (!File.Exists(path))
        {
            return AudioConfig.Default;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new AudioConfig(
                Read(root, "menuMusic", AudioConfig.Default.MenuMusic),
                Read(root, "defaultAmbient", AudioConfig.Default.DefaultAmbient),
                Read(root, "uiClickSound", AudioConfig.Default.UiClickSound),
                Read(root, "notificationSound", AudioConfig.Default.NotificationSound));
        }
        catch (JsonException)
        {
            return AudioConfig.Default;
        }
    }

    private static string Read(JsonElement root, string propertyName, string fallback) =>
        root.TryGetProperty(propertyName, out var value) ? value.GetString() ?? fallback : fallback;

    private void ReleaseOneShot(MediaPlayer player)
    {
        player.Stop();
        player.Close();
        _oneShots.Remove(player);
    }

    private static void Loop(MediaPlayer player)
    {
        player.Position = TimeSpan.Zero;
        player.Play();
    }

    private static void StopAndClose(MediaPlayer player)
    {
        player.Stop();
        player.Close();
    }

    private double Master => Math.Clamp(_settings.MasterVolume / 100d, 0, 1);
    private double MusicVolume => Master * Math.Clamp(_settings.MusicVolume / 100d, 0, 1);
    private double AmbientVolume => Master * Math.Clamp(_settings.AmbientVolume / 100d, 0, 1);
    private double EffectsVolume => Master * Math.Clamp(_settings.EffectsVolume / 100d, 0, 1);
    private double VoiceVolume => Master * Math.Clamp(_settings.VoiceVolume / 100d, 0, 1);
    private double NotificationVolume => EffectsVolume * Math.Clamp(_settings.NotificationVolume / 100d, 0, 1);
    private double UiClickVolume => EffectsVolume * 0.55;

    private double VolumeFor(AudioChannel channel) => channel switch
    {
        AudioChannel.Notification => NotificationVolume,
        AudioChannel.UiClick => UiClickVolume,
        _ => EffectsVolume
    };

    private enum AudioChannel
    {
        Effects,
        UiClick,
        Notification
    }

    private sealed record AudioConfig(
        string MenuMusic,
        string DefaultAmbient,
        string UiClickSound,
        string NotificationSound)
    {
        public static AudioConfig Default { get; } = new(
            "audio/music/Where_the_Iron_Holds.mp3",
            "audio/ambient/apartment_night.wav",
            "audio/ui/ui_click.mp3",
            "audio/ui/notification.mp3");
    }
}
