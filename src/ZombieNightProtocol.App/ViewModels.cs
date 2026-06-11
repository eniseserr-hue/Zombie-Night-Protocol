using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZombieNightProtocol.Core;
using ZombieNightProtocol.Infrastructure;

namespace ZombieNightProtocol.App;

public enum AppScreen
{
    Loading,
    MainMenu,
    PlayMenu,
    CharacterSelection,
    Intro,
    Saves,
    Settings,
    Help,
    Game,
    Ending,
    UpdateRequired,
    Error
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IStoryRepository _storyRepository;
    private readonly ICharacterRepository _characterRepository;
    private readonly ISaveService _saveService;
    private readonly ISettingsService _settingsService;
    private readonly IGameDiagnostics _diagnostics;
    private readonly GitHubUpdateService _updateService;
    private readonly PatchDownloader _patchDownloader;
    private readonly ApplicationPaths _paths;
    private readonly AudioService _audio;
    private StoryPackage? _story;
    private IReadOnlyList<CharacterDefinition> _characters = [];
    private GameSettings? _settingsSnapshot;
    private CancellationTokenSource? _settingsToastCancellation;
    private bool _ignoreSettingsChanges;

    [ObservableProperty] private AppScreen _screen = AppScreen.Loading;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _loadingStep = "Güncellemeler kontrol ediliyor...";
    [ObservableProperty] private double _loadingProgress;
    [ObservableProperty] private CharacterDefinition? _selectedCharacter;
    [ObservableProperty] private bool _isConfirmingCharacter;
    [ObservableProperty] private bool _isIntroMuted;
    [ObservableProperty] private GameSettings _settings = new();
    [ObservableProperty] private GameSessionViewModel? _game;
    [ObservableProperty] private UpdateManifest? _requiredUpdate;
    [ObservableProperty] private int _updateProgress;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private bool _isUpdateCheckFailed;
    [ObservableProperty] private bool _isSettingsToastVisible;
    [ObservableProperty] private string _settingsToastText = "";
    [ObservableProperty] private bool _isSettingsDirty;

    public MainViewModel(
        IStoryRepository storyRepository,
        ICharacterRepository characterRepository,
        ISaveService saveService,
        ISettingsService settingsService,
        IGameDiagnostics diagnostics,
        GitHubUpdateService updateService,
        PatchDownloader patchDownloader,
        ApplicationPaths paths,
        AudioService audio)
    {
        _storyRepository = storyRepository;
        _characterRepository = characterRepository;
        _saveService = saveService;
        _settingsService = settingsService;
        _diagnostics = diagnostics;
        _updateService = updateService;
        _patchDownloader = patchDownloader;
        _paths = paths;
        _audio = audio;
    }

    public ObservableCollection<CharacterDefinition> Characters { get; } = [];
    public ObservableCollection<SaveSlotViewModel> SaveSlots { get; } = [];
    public IReadOnlyList<HelpSectionViewModel> HelpSections { get; } = HelpSectionViewModel.CreateAll();
    public IReadOnlyList<string> TextSpeeds { get; } = ["Yavaş", "Normal", "Hızlı", "Anında"];
    public IReadOnlyList<string> TextAnimationModes { get; } = ["Anında", "Daktilo", "Satır Satır", "Yumuşak Belirme", "Kelime Kelime"];
    public IReadOnlyList<string> Resolutions { get; } = ["1024x768", "1280x800", "1600x900", "1920x1080"];
    public IReadOnlyList<string> Languages { get; } = ["Türkçe"];
    public string VersionText => $"Sürüm {GameConstants.Version}";
    public string InstagramIconPath => "images/ui/social/instagram_icon.png";
    public string ContinueText => HasSave ? "➦  Kaldığın Yerden Devam Et" : "➦  Devam Et";

    public bool IsLoading => Screen == AppScreen.Loading;
    public bool IsMainMenu => Screen == AppScreen.MainMenu;
    public bool IsPlayMenu => Screen == AppScreen.PlayMenu;
    public bool IsCharacterSelection => Screen == AppScreen.CharacterSelection;
    public bool IsIntro => Screen == AppScreen.Intro;
    public bool IsSaves => Screen == AppScreen.Saves;
    public bool IsSettings => Screen == AppScreen.Settings;
    public bool IsHelp => Screen == AppScreen.Help;
    public bool IsGame => Screen == AppScreen.Game;
    public bool IsEnding => Screen == AppScreen.Ending;
    public bool IsUpdateRequired => Screen == AppScreen.UpdateRequired;
    public bool IsError => Screen == AppScreen.Error;
    public bool HasSave => SaveSlots.Any(slot => !slot.IsEmpty && !slot.IsCorrupted);
    public bool HasSelectedCharacter => SelectedCharacter is not null;
    public string UpdateReleaseNotes => RequiredUpdate is null ? "" : string.Join(Environment.NewLine, RequiredUpdate.ReleaseNotes.Select(note => $"• {note}"));
    public string UpdatePackageSize => RequiredUpdate is null ? "" : $"{RequiredUpdate.EffectivePackageSize / 1024d / 1024d:N1} MB";
    public Uri IntroVideoSource => new(Path.Combine(AppContext.BaseDirectory, "content", "video", "intro.mov"));
    public string IntroMuteText => IsIntroMuted ? "Sesi Aç" : "Sesi Kapat";

    partial void OnScreenChanged(AppScreen value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsMainMenu));
        OnPropertyChanged(nameof(IsPlayMenu));
        OnPropertyChanged(nameof(IsCharacterSelection));
        OnPropertyChanged(nameof(IsIntro));
        OnPropertyChanged(nameof(IsSaves));
        OnPropertyChanged(nameof(IsSettings));
        OnPropertyChanged(nameof(IsHelp));
        OnPropertyChanged(nameof(IsGame));
        OnPropertyChanged(nameof(IsEnding));
        OnPropertyChanged(nameof(IsUpdateRequired));
        OnPropertyChanged(nameof(IsError));
    }

    partial void OnIsIntroMutedChanged(bool value) => OnPropertyChanged(nameof(IntroMuteText));

    partial void OnRequiredUpdateChanged(UpdateManifest? value) =>
        OnRequiredUpdateChangedCore();

    private void OnRequiredUpdateChangedCore()
    {
        OnPropertyChanged(nameof(UpdateReleaseNotes));
        OnPropertyChanged(nameof(UpdatePackageSize));
        DownloadUpdateCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        var loadingTimeline = RunLoadingTimelineAsync();
        try
        {
            Settings = await _settingsService.LoadAsync();
            if (!Languages.Contains(Settings.Language))
            {
                Settings.Language = "Türkçe";
            }
            ApplyDisplaySettings(Settings);
            _audio.ApplySettings(Settings);
            _audio.StartMenuMusic(_audio.MenuMusicPath, fadeIn: true);

            _characters = await _characterRepository.LoadAsync();
            _story = await _storyRepository.LoadAsync();
            Characters.Clear();
            foreach (var character in _characters)
            {
                Characters.Add(character);
            }

            await RefreshSaveSlotsAsync();
            var update = await _updateService.CheckAsync(Version.Parse(GameConstants.Version));
            StatusMessage = "";

            await loadingTimeline;
            LoadingProgress = 100;
            if (update.CheckFailed)
            {
#if DEBUG
                Screen = AppScreen.MainMenu;
#else
                IsUpdateCheckFailed = true;
                UpdateStatus = "Güncelleme sunucusuna bağlanılamadı. Bağlantıyı kontrol edip yeniden dene.";
                Screen = AppScreen.UpdateRequired;
#endif
            }
            else if (update.IsRequired)
            {
                RequiredUpdate = update.Manifest;
                Screen = AppScreen.UpdateRequired;
            }
            else
            {
                Screen = AppScreen.MainMenu;
            }
        }
        catch (Exception exception)
        {
            _diagnostics.Error(exception.ToString());
            StatusMessage = "İçerik yüklenemedi. Ayrıntılar log dosyasına kaydedildi.";
            Screen = AppScreen.Error;
        }
    }

    private async Task RunLoadingTimelineAsync()
    {
        var timer = Stopwatch.StartNew();
        const double totalSeconds = 20;
        while (timer.Elapsed.TotalSeconds < totalSeconds)
        {
            var elapsed = timer.Elapsed.TotalSeconds;
            LoadingStep = elapsed switch
            {
                < 4 => "Güncellemeler kontrol ediliyor...",
                < 10 => "Envanter hazırlanıyor...",
                < 15 => "Hikâye hazırlanıyor...",
                _ => "Kararlar hazırlanıyor..."
            };
            LoadingProgress = Math.Clamp(elapsed / totalSeconds * 100, 0, 99.8);
            await Task.Delay(50);
        }
        LoadingProgress = 100;
    }

    [RelayCommand]
    private void ShowMainMenu()
    {
        if (Screen is AppScreen.Game or AppScreen.Ending)
        {
            _audio.ReturnToMenu(_audio.MenuMusicPath);
        }
        IsConfirmingCharacter = false;
        Screen = AppScreen.MainMenu;
    }

    [RelayCommand]
    private void ShowPlayMenu() => Screen = AppScreen.PlayMenu;

    [RelayCommand]
    private void StartNewGame()
    {
        if (Screen == AppScreen.Ending)
        {
            _audio.ReturnToMenu(_audio.MenuMusicPath);
        }
        SelectedCharacter = null;
        IsConfirmingCharacter = false;
        Screen = AppScreen.CharacterSelection;
    }

    [RelayCommand]
    private void SelectCharacter(CharacterDefinition character) => SelectedCharacter = character;

    [RelayCommand(CanExecute = nameof(CanConfirmCharacter))]
    private void RequestCharacterConfirmation() => IsConfirmingCharacter = true;

    private bool CanConfirmCharacter() => SelectedCharacter is not null;

    partial void OnSelectedCharacterChanged(CharacterDefinition? value)
    {
        RequestCharacterConfirmationCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelectedCharacter));
    }

    [RelayCommand]
    private void CancelCharacterConfirmation() => IsConfirmingCharacter = false;

    [RelayCommand]
    private void ConfirmCharacter()
    {
        if (SelectedCharacter is null || _story is null)
        {
            return;
        }

        var state = GameStateFactory.Create(SelectedCharacter, _characters, _story.StartSceneId);
        Game = new GameSessionViewModel(
            new StoryEngine(_story, _characters, _diagnostics),
            state,
            _characters,
            _saveService,
            Settings,
            _audio,
            OnGameEnded,
            ReturnToPlayMenuFromGame);
        IsConfirmingCharacter = false;
        IsIntroMuted = false;
        if (File.Exists(IntroVideoSource.LocalPath))
        {
            _audio.StopAll();
            Screen = AppScreen.Intro;
        }
        else
        {
            StartPreparedGame();
        }
    }

    [RelayCommand]
    private void SkipIntro() => StartPreparedGame();

    [RelayCommand]
    private void ToggleIntroMute() => IsIntroMuted = !IsIntroMuted;

    private void StartPreparedGame()
    {
        if (Game is null)
        {
            return;
        }
        Screen = AppScreen.Game;
        Game.Start();
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task ContinueGameAsync()
    {
        if (_story is null)
        {
            return;
        }
        var latest = SaveSlots.Where(slot => !slot.IsEmpty && !slot.IsCorrupted).OrderByDescending(slot => slot.SavedAt).FirstOrDefault();
        if (latest is null)
        {
            return;
        }
        var state = await _saveService.LoadAsync(latest.Slot);
        if (state is null)
        {
            await RefreshSaveSlotsAsync();
            return;
        }
        Game = new GameSessionViewModel(
            new StoryEngine(_story, _characters, _diagnostics),
            state,
            _characters,
            _saveService,
            Settings,
            _audio,
            OnGameEnded,
            ReturnToPlayMenuFromGame);
        Screen = AppScreen.Game;
        Game.Start();
    }

    private bool CanContinue() => HasSave;

    [RelayCommand]
    private async Task ShowSavesAsync()
    {
        await RefreshSaveSlotsAsync();
        Screen = AppScreen.Saves;
    }

    [RelayCommand]
    private async Task LoadSlotAsync(int slot)
    {
        if (_story is null)
        {
            return;
        }
        var state = await _saveService.LoadAsync(slot);
        if (state is null)
        {
            StatusMessage = $"{slot}. kayıt yuvası yüklenemedi.";
            return;
        }
        Game = new GameSessionViewModel(
            new StoryEngine(_story, _characters, _diagnostics),
            state,
            _characters,
            _saveService,
            Settings,
            _audio,
            OnGameEnded,
            ReturnToPlayMenuFromGame);
        Screen = AppScreen.Game;
        Game.Start();
    }

    [RelayCommand]
    private async Task DeleteSlotAsync(int slot)
    {
        await _saveService.DeleteAsync(slot);
        await RefreshSaveSlotsAsync();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        _ignoreSettingsChanges = true;
        _settingsSnapshot = CloneSettings(Settings);
        IsSettingsDirty = false;
        StatusMessage = "";
        Screen = AppScreen.Settings;
        _ignoreSettingsChanges = false;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await _settingsService.SaveAsync(Settings);
        _audio.ApplySettings(Settings);
        ApplyDisplaySettings(Settings);
        _settingsSnapshot = CloneSettings(Settings);
        IsSettingsDirty = false;
        StatusMessage = "";
        await ShowSettingsToastAsync("Ayarlar kaydedildi.");
    }

    [RelayCommand]
    private void CancelSettings()
    {
        if (_settingsSnapshot is not null)
        {
            _ignoreSettingsChanges = true;
            Settings = CloneSettings(_settingsSnapshot);
            _audio.ApplySettings(Settings);
            ApplyDisplaySettings(Settings);
            _ignoreSettingsChanges = false;
        }
        IsSettingsDirty = false;
        StatusMessage = "";
    }

    [RelayCommand]
    private void ResetSettings()
    {
        Settings = new GameSettings();
        _audio.ApplySettings(Settings);
        IsSettingsDirty = true;
        StatusMessage = "";
    }

    [RelayCommand]
    private void ExitSettings()
    {
        if (!IsSettingsDirty)
        {
            Screen = AppScreen.MainMenu;
        }
    }

    public void SettingsChanged(bool previewAudio = false)
    {
        if (_ignoreSettingsChanges || !IsSettings || _settingsSnapshot is null)
        {
            return;
        }
        if (previewAudio)
        {
            _audio.ApplySettings(Settings);
        }
        IsSettingsDirty = !SettingsEqual(Settings, _settingsSnapshot);
    }

    public void PlayUiClick() => _audio.PlayUiClick();

    [RelayCommand]
    private void ShowHelp() => Screen = AppScreen.Help;

    [RelayCommand]
    private void OpenSaveFolder() => OpenFolder(_settingsService.SavesFolder);

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(_settingsService.LogsFolder);

    [RelayCommand]
    private void OpenInstagram() => Process.Start(new ProcessStartInfo(
        "https://www.instagram.com/bullukespor/") { UseShellExecute = true });

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task DownloadUpdateAsync()
    {
        if (RequiredUpdate is null)
        {
            return;
        }

        IsUpdating = true;
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        try
        {
            var timer = Stopwatch.StartNew();
            var progress = new Progress<(long Downloaded, long Total)>(value =>
            {
                UpdateProgress = (int)Math.Clamp(value.Downloaded * 100L / Math.Max(1L, value.Total), 0, 100);
                var speed = value.Downloaded / Math.Max(0.2, timer.Elapsed.TotalSeconds);
                var remaining = Math.Max(0, value.Total - value.Downloaded);
                var eta = TimeSpan.FromSeconds(remaining / Math.Max(1, speed));
                UpdateStatus = $"İndiriliyor • %{UpdateProgress} • {speed / 1024d / 1024d:N1} MB/sn • kalan {eta:mm\\:ss}";
            });
            var packagePath = await _patchDownloader.DownloadPackageAsync(RequiredUpdate, _paths.Staging, progress);
            UpdateStatus = "Paket doğrulandı. Güncelleyici hazırlanıyor...";
            var manifestPath = Path.Combine(_paths.Staging, "manifest.json");
            await AtomicFile.WriteJsonAsync(manifestPath, RequiredUpdate);
            var updaterPath = Path.Combine(AppContext.BaseDirectory, "ZombieNightProtocol.Updater.exe");
            if (!File.Exists(updaterPath))
            {
                throw new FileNotFoundException("Güncelleyici bulunamadı.", updaterPath);
            }
            var temporaryUpdater = Path.Combine(_paths.Updates, "ZombieNightProtocol.Updater.exe");
            Directory.CreateDirectory(_paths.Updates);
            File.Copy(updaterPath, temporaryUpdater, true);
            PatchDownloader.StartUpdater(temporaryUpdater, AppContext.BaseDirectory, _paths.Staging, manifestPath, packagePath, Environment.ProcessId);
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            _diagnostics.Error(exception.ToString());
            UpdateStatus = "Güncelleme indirilemedi. Ağ bağlantısını kontrol edip tekrar dene.";
            IsUpdating = false;
            DownloadUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanDownloadUpdate() => RequiredUpdate is not null && !IsUpdating;

    [RelayCommand]
    private async Task RetryUpdateCheckAsync()
    {
        IsUpdateCheckFailed = false;
        UpdateStatus = "Güncelleme sunucusu yeniden kontrol ediliyor...";
        var update = await _updateService.CheckAsync(Version.Parse(GameConstants.Version));
        if (update.CheckFailed)
        {
            IsUpdateCheckFailed = true;
            UpdateStatus = "Sunucuya hâlâ ulaşılamıyor. İnternet bağlantısını kontrol et.";
            return;
        }
        if (update.IsRequired)
        {
            RequiredUpdate = update.Manifest;
            UpdateStatus = "Zorunlu güncelleme hazır.";
            return;
        }
        Screen = AppScreen.MainMenu;
    }

    [RelayCommand]
    private void ReturnToCheckpoint()
    {
        if (Game?.RestoreCheckpoint() == true)
        {
            Screen = AppScreen.Game;
        }
    }

    [RelayCommand]
    private void RestartAfterEnding() => StartNewGame();

    private void OnGameEnded() => Screen = AppScreen.Ending;

    private void ReturnToPlayMenuFromGame()
    {
        _audio.ReturnToMenu(_audio.MenuMusicPath);
        Screen = AppScreen.PlayMenu;
    }

    public async Task SaveActiveSessionAsync()
    {
        if (Game is null || Game.State.Ending != EndingKind.None)
        {
            return;
        }
        await Game.SaveEmergencySnapshotAsync();
    }

    public void RecoverFromUiError()
    {
        Game?.Stop();
        _audio.ReturnToMenu(_audio.MenuMusicPath);
        IsConfirmingCharacter = false;
        StatusMessage = "Beklenmeyen bir arayüz hatasından sonra ana menüye dönüldü.";
        Screen = AppScreen.MainMenu;
    }

    private async Task ShowSettingsToastAsync(string text)
    {
        _settingsToastCancellation?.Cancel();
        _settingsToastCancellation = new CancellationTokenSource();
        SettingsToastText = text;
        IsSettingsToastVisible = true;
        try
        {
            await Task.Delay(2400, _settingsToastCancellation.Token);
            IsSettingsToastVisible = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshSaveSlotsAsync()
    {
        var slots = await _saveService.GetSlotsAsync();
        SaveSlots.Clear();
        foreach (var slot in slots)
        {
            SaveSlots.Add(new SaveSlotViewModel(slot));
        }
        OnPropertyChanged(nameof(HasSave));
        OnPropertyChanged(nameof(ContinueText));
        ContinueGameCommand.NotifyCanExecuteChanged();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private static GameSettings CloneSettings(GameSettings source) => new()
    {
        MasterVolume = source.MasterVolume,
        MusicVolume = source.MusicVolume,
        AmbientVolume = source.AmbientVolume,
        EffectsVolume = source.EffectsVolume,
        VoiceVolume = source.VoiceVolume,
        NotificationVolume = source.NotificationVolume,
        NotificationSounds = source.NotificationSounds,
        TextSpeed = source.TextSpeed,
        FullScreen = source.FullScreen,
        Resolution = source.Resolution,
        UiScale = source.UiScale,
        TextAnimation = source.TextAnimation,
        TextAnimationMode = source.TextAnimationMode,
        TopMessageNotifications = source.TopMessageNotifications,
        AutoSave = source.AutoSave,
        Language = source.Language,
        UpdateChannel = source.UpdateChannel
    };

    private static bool SettingsEqual(GameSettings left, GameSettings right) =>
        left.MasterVolume == right.MasterVolume &&
        left.MusicVolume == right.MusicVolume &&
        left.AmbientVolume == right.AmbientVolume &&
        left.EffectsVolume == right.EffectsVolume &&
        left.VoiceVolume == right.VoiceVolume &&
        left.NotificationVolume == right.NotificationVolume &&
        left.NotificationSounds == right.NotificationSounds &&
        left.TextSpeed == right.TextSpeed &&
        left.FullScreen == right.FullScreen &&
        left.Resolution == right.Resolution &&
        left.UiScale == right.UiScale &&
        left.TextAnimation == right.TextAnimation &&
        left.TextAnimationMode == right.TextAnimationMode &&
        left.TopMessageNotifications == right.TopMessageNotifications &&
        left.AutoSave == right.AutoSave &&
        left.Language == right.Language;

    private static void ApplyDisplaySettings(GameSettings settings)
    {
        if (Application.Current.MainWindow is not Window window)
        {
            return;
        }

        if (settings.FullScreen)
        {
            window.WindowStyle = WindowStyle.None;
            window.WindowState = WindowState.Maximized;
            if (window is MainWindow fullScreenWindow)
            {
                fullScreenWindow.ApplyUiScale(settings.UiScale);
            }
            return;
        }

        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.WindowState = WindowState.Normal;
        var dimensions = settings.Resolution.Split('x');
        if (dimensions.Length == 2 &&
            double.TryParse(dimensions[0], out var width) &&
            double.TryParse(dimensions[1], out var height))
        {
            var workArea = SystemParameters.WorkArea;
            window.Width = Math.Min(Math.Max(window.MinWidth, width), workArea.Width);
            window.Height = Math.Min(Math.Max(window.MinHeight, height), workArea.Height);
            window.Left = workArea.Left + Math.Max(0, (workArea.Width - window.Width) / 2);
            window.Top = workArea.Top + Math.Max(0, (workArea.Height - window.Height) / 2);
        }
        if (window is MainWindow mainWindow)
        {
            mainWindow.ApplyUiScale(settings.UiScale);
        }
    }
}

public sealed record HelpSectionViewModel(string Icon, string Title, string Body)
{
    public static IReadOnlyList<HelpSectionViewModel> CreateAll() =>
    [
        new("◈", "Oyuna Genel Bakış", "Türkiye'de geçen karanlık bir hayatta kalma hikâyesinde bir ana karakter seçer, diğer altı kişiyle birlikte geceyi aşmaya çalışırsın."),
        new("➦", "Hikâye Nasıl İlerler?", "Anlatımı oku, seçeneklerden birini seç, sağ panelde sonucunu ve riskini incele; yalnızca İlerle komutuyla kararı uygula."),
        new("◈", "Karakter Seçimi", "Seçtiğin kişi ana karakter olur. Portre, uzmanlık ve başlangıç eşyaları oyuna taşınır; diğer karakterler ekipte kalır."),
        new("◆", "Karakter Uzmanlıkları", "Güç, Teknik, Analiz, Çeviklik, Liderlik, Tıp ve Mekanik farklı koşulları açar. Her uzmanlık başka bir rota üretir."),
        new("➦", "Normal Seçimler", "Standart seçenekler doğrudan bir beceri istemez; yine de gürültü, kaynak, güven veya rota üzerinde kalıcı sonuç doğurabilir."),
        new("⚠", "Tehlikeli Seçimler", "Mat uyarı rengiyle gösterilir. Sağlık, tehdit, enfeksiyon veya karakter kaybı riski taşıyabilir."),
        new("⛓", "Kilitli Seçimler", "Gereken özellik, eşya, güven seviyesi veya hikâye bayrağı sağlanmadığında seçilemez; gerekçe sağ panelde görünür."),
        new("◈", "Özellik Gereksinimleri", "Köşeli parantezli etiketler hangi uzmanlığın kontrol edildiğini söyler. Değer, seçtiğin ana karakterin istatistiğinden okunur."),
        new("◇", "Envanter", "Eşyaların miktarı, ağırlığı, kullanım durumu ve etkisi Durum panelinde görülür. Bazı eşyalar yeni hikâye yolları açar."),
        new("＋", "Sağlık", "Yaralanma ve seçim etkileri sağlığı düşürür. Sıfıra ulaşmak ölüm sonucunu tetikleyebilir."),
        new("◌", "Açlık", "Zaman ve kaynak kıtlığı açlığı artırır. Uzun süre yüksek kalması fiziksel dayanıklılığı etkiler."),
        new("◌", "Susuzluk", "Susuzluk açlıktan daha hızlı tehlikeli hâle gelir. İçecekleri doğru zamanda kullan."),
        new("⌁", "Enfeksiyon", "Enfeksiyon oranı mevcut durumu, enfeksiyon riski ise maruz kalma ihtimalini anlatır. İkisi aynı değildir."),
        new("◆", "Moral", "Kayıplar, güven ve umut moral değerini değiştirir. Düşük moral ekip davranışlarını ve kararlarını zorlaştırır."),
        new("◈", "Akıl Sağlığı", "Travmatik seçimler ve baskı akıl sağlığını düşürür. Durum panelindeki zihinsel değerlendirmeyi takip et."),
        new("◇", "Güven Sistemi", "Her ekip üyesinin sana karşı güveni ayrıdır. Güven, özel konuşmaları ve yardım seçeneklerini açabilir."),
        new("◌", "Karakter Ruh Hâlleri", "Ruh hâli, karakterin o anki baskıya nasıl tepki verdiğini gösterir ve hikâye olaylarıyla değişir."),
        new("◈", "Karakter Düşünceleri", "Ekip kartlarındaki son düşünceler açıkça söylenmeyen gerilimi gösterir. Bazı bilgiler güvenle açılır."),
        new("⚠", "Süreli Seçimler", "Sayaç sıfıra ulaşınca sahnenin varsayılan kararı otomatik uygulanır. Kritik süreli kararlar geri alınamayabilir."),
        new("↶", "Geri Alma Sistemi", "Normal bir kararın hemen ardından önceki durum görüntüsüne dönebilirsin. Kritik kararlar bu hakkı kapatır."),
        new("◆", "Kontrol Noktaları", "Önemli sahnelerde güvenli bir durum görüntüsü tutulur. Ölüm ekranından son kontrol noktasına dönülebilir."),
        new("×", "Ölüm Sistemi", "Ana karakterin ölümü veya ölüm sonu hikâyeyi bitirir. Ekip üyeleri de kalıcı olarak kaybedilebilir."),
        new("▤", "Kayıt Yuvaları", "Üç yuva bulunur. Dolu yuvanın üzerine yazmadan önce onay istenir; kayıt atomik yazılır ve yedeklenir."),
        new("⌖", "Harita", "İlk ana rota İstanbul ➦ Ankara ➦ Sivas ➦ Malatya'dır. Açılan ve henüz doğrulanmayan yollar haritada ayrılır."),
        new("◆", "Görevler", "Aktif amaçlar, öncelik, ilerleme ve kısa ipuçlarıyla gösterilir. Hikâye seçimleri görev durumunu değiştirebilir."),
        new("▥", "Geçmiş", "Karar günlüğü; gün, saat, konum ve kategoriyle önemli olayları ters kronolojik sırada saklar."),
        new("↻", "Güncellemeler", "Açılışta yeni sürüm kontrol edilir. Ağ yoksa oyun çevrimdışı açılır; zorunlu bir güncelleme varsa devam etmeden önce bildirilir."),
        new("⌨", "Klavye Kısayolları", "Tab ve Shift+Tab odaklar arasında gezer. Enter veya Boşluk seçer. Esc açık Durum ya da Kayıt panelini kapatır."),
        new("◈", "İpuçları", "Önce kilit gerekçesini oku, sonra gürültü ve tehdit seviyesini karşılaştır. Kaynak kadar ekip güveni de bir çıkış yoludur."),
        new("◈", "Sürüm Bilgisi", $"Sürüm {GameConstants.Version}")
    ];
}

public sealed partial class GameSessionViewModel : ObservableObject
{
    private readonly StoryEngine _engine;
    private readonly ISaveService _saveService;
    private readonly GameSettings _settings;
    private readonly AudioService _audio;
    private readonly Action _onEnded;
    private readonly Action _onExit;
    private CancellationTokenSource? _typingCancellation;
    private CancellationTokenSource? _timerCancellation;
    private CancellationTokenSource? _clockCancellation;
    private CancellationTokenSource? _topMessageCancellation;
    private CancellationTokenSource? _periodicSaveCancellation;
    private CancellationTokenSource? _voiceProgressCancellation;
    private readonly Queue<TopMessageViewModel> _topNotificationQueue = new();
    private bool _isTopNotificationWorkerRunning;
    private string _fullNarrative = "";

    [ObservableProperty] private StoryScene? _scene;
    [ObservableProperty] private string _displayedNarrative = "";
    [ObservableProperty] private string _speaker = "";
    [ObservableProperty] private string _dialogue = "";
    [ObservableProperty] private bool _isStatusPanelOpen;
    [ObservableProperty] private string _statusTab = "Karakter";
    [ObservableProperty] private int _countdownSeconds;
    [ObservableProperty] private int _countdownMaximum;
    [ObservableProperty] private bool _isTimedChoice;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private bool _isSavePanelOpen;
    [ObservableProperty] private bool _isConfirmingOverwrite;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private int _pendingSaveSlot;
    [ObservableProperty] private int _saveProgress;
    [ObservableProperty] private string _saveProgressText = "Kayıt yuvası seç";
    [ObservableProperty] private string _activeSceneImage = "images/scenes/fallback.webp";
    [ObservableProperty] private ChoiceViewModel? _selectedChoice;
    [ObservableProperty] private string _dialogueSpeakerId = "";
    [ObservableProperty] private bool _isMessagePanelOpen;
    [ObservableProperty] private bool _isInventoryUsePanelOpen;
    [ObservableProperty] private string _inventoryFeedback = "";
    [ObservableProperty] private HelpOptionViewModel? _selectedHelpOption;
    [ObservableProperty] private int _narrativeFadeToken;
    [ObservableProperty] private int _shakeRequestToken;
    [ObservableProperty] private double _shakeIntensity;
    [ObservableProperty] private bool _isNoiseEffectVisible;
    [ObservableProperty] private double _noiseOpacity;
    [ObservableProperty] private string _sceneHint = "";
    [ObservableProperty] private VoiceMessageDefinition? _sceneVoiceMessage;
    [ObservableProperty] private bool _isVoiceTranscriptVisible;
    [ObservableProperty] private bool _isVoiceMessagePlaying;
    [ObservableProperty] private bool _isVoiceChoiceConfirmed;
    [ObservableProperty] private string _voiceMessageTimeText = "00:00 / --:--";
    [ObservableProperty] private int _voiceProgress;

    public GameSessionViewModel(
        StoryEngine engine,
        GameState state,
        IReadOnlyList<CharacterDefinition> characters,
        ISaveService saveService,
        GameSettings settings,
        AudioService audio,
        Action onEnded,
        Action onExit)
    {
        _engine = engine;
        State = state;
        CharacterDefinitions = characters.ToDictionary(character => character.Id, StringComparer.OrdinalIgnoreCase);
        _saveService = saveService;
        _settings = settings;
        _audio = audio;
        _onEnded = onEnded;
        _onExit = onExit;
    }

    public GameState State { get; }
    public IReadOnlyDictionary<string, CharacterDefinition> CharacterDefinitions { get; }
    public ObservableCollection<ChoiceViewModel> Choices { get; } = [];
    public ObservableCollection<SaveSlotViewModel> SaveSlots { get; } = [];
    public ObservableCollection<TopMessageViewModel> VisibleTopMessages { get; } = [];
    public ObservableCollection<HelpOptionViewModel> HelpOptions { get; } = [];
    public CharacterDefinition PlayerDefinition => CharacterDefinitions[State.SelectedCharacterId];
    public CharacterState Player => State.Characters.First(character => character.IsPlayer);
    public IEnumerable<CharacterState> Team => State.Characters.Where(character => !character.IsPlayer && IsCharacterKnown(character.Id));
    public IEnumerable<RelationshipDisplay> TeamRelationships => Team.Select(character => new RelationshipDisplay(
        character,
        CharacterDefinitions.GetValueOrDefault(character.Id),
        State.Relationships.FirstOrDefault(relationship => relationship.CharacterId == character.Id)?.Trust ?? 50,
        State.Relationships.FirstOrDefault(relationship => relationship.CharacterId == character.Id)?.Status ?? "Temkinli",
        TeamLastEvent(character)));
    public IEnumerable<InventoryDisplay> Inventory => State.Inventory.Select(item => new InventoryDisplay(item));
    public IEnumerable<InventoryDisplay> UsableInventory => State.Inventory
        .Where(IsItemRelevant)
        .Select(item => new InventoryDisplay(item));
    public IEnumerable<MessageDisplay> MessageHistory => State.Messages
        .OrderByDescending(message => message.Day)
        .ThenByDescending(message => message.GameTime)
        .Select(message => new MessageDisplay(message));
    public int UnreadMessageCount => State.Messages.Count(message => !message.IsRead);
    public IEnumerable<JournalEntry> HistoryEntries => State.Journal.OrderByDescending(entry => entry.Timestamp);
    public IReadOnlyList<ScenarioTaskDisplay> ScenarioTasks => CreateScenarioTasks();
    public bool IsCharacterTab => StatusTab == "Karakter";
    public bool IsTeamTab => StatusTab == "Ekip";
    public bool IsInventoryTab => StatusTab == "Envanter";
    public bool IsMapTab => StatusTab == "Harita";
    public bool IsTasksTab => StatusTab == "Görevler";
    public bool IsHistoryTab => StatusTab == "Geçmiş";
    public string TimeText => $"{State.Time:hh\\:mm}";
    public string EndingTitle => State.Ending == EndingKind.Death ? "Yolculuğun sona erdi." : "Bölüm tamamlandı.";
    public string EndingSummary => State.EndingReason;
    public string PlayedTimeText => TimeSpan.FromSeconds(State.PlayedSeconds).ToString(@"hh\:mm\:ss");
    public int AliveCount => State.Characters.Count(character => character.IsAlive && (character.IsPlayer || IsCharacterKnown(character.Id)));
    public bool CanRestoreCheckpoint => State.CheckpointSnapshot is not null;
    public bool HasSelectedChoice => SelectedChoice is not null;
    public bool IsChapterEndingScene => Scene?.Id == "s01c01_scene16_ending_narration";
    public string SelectedChoiceImage =>
        string.IsNullOrWhiteSpace(SelectedChoice?.PreviewImage)
            ? ActiveSceneImage
            : SelectedChoice.PreviewImage;
    public string SelectionSummary => SelectedChoice?.SelectionSummary ?? "";
    public string SelectionRisk => SelectedChoice?.RiskLevel ?? "";
    public string SelectionRequirement => SelectedChoice?.RequirementText ?? "";
    public string PhysicalAssessment => State.Health switch
    {
        >= 80 => "Dinç ve hareket kabiliyeti yüksek",
        >= 55 => "Yorgun ama göreve hazır",
        >= 30 => "Yaralı, dikkatli hareket etmeli",
        _ => "Kritik durumda"
    };
    public string MentalAssessment => State.Sanity switch
    {
        >= 80 => "Odaklanmış",
        >= 55 => "Gergin",
        >= 30 => "Baskı altında",
        _ => "Çöküş sınırında"
    };
    public string ThreatAssessment => State.ThreatLevel switch
    {
        >= 75 => "Kuşatma riski",
        >= 50 => "Yakın tehdit",
        >= 25 => "İzleniyor olabilirsiniz",
        _ => "Şimdilik düşük"
    };
    public string NoiseAssessment => State.NoiseLevel switch
    {
        >= 70 => "Çok yüksek",
        >= 40 => "Duyulabilir",
        >= 20 => "Kontrollü",
        _ => "Sessiz"
    };
    public double MapCurrentX => CurrentMapPoint().X;
    public double MapCurrentY => CurrentMapPoint().Y;
    public string MapLocationLabel => Scene?.Location ?? State.Location;
    public string MapStatus => Scene?.Id switch
    {
        "s01c01_scene01_wakeup" => "Sağlık merkezinde tek başınasın. Henüz güvenli rota yok.",
        "s01c01_scene02_clinic_corridor" or "s01c01_scene03_first_infected" or "s01c01_scene04_hidden_stranger" or "s01c01_scene05_exit_together" => "Sağlık merkezinden çıkış aranıyor. Gürültü koridorlara yayılmadan dışarı ulaşmalısın.",
        "s01c01_scene06_silent_istanbul" => "Esenler sokaklarına çıktın. Kırmızı işaretler ve sessiz hatlar takip edilebilir.",
        "s01c01_scene07_first_shelter" or "s01c01_scene08_power_antenna" or "s01c01_scene09_derya_recording" or "s01c01_scene10_apartment_fall" => "Apartman ve çatı hattı aktif. Telsiz kaydı kuzey kapısı yönünü güçlendiriyor.",
        "s01c01_scene11_bedo_arrives" or "s01c01_scene12_first_night_team" => "Çatı hattından terzi atölyesine sığınıldı. Ekip yavaş yavaş toplanıyor.",
        "s01c01_scene13_first_supply_run" or "s01c01_scene14_meet_busra" => "Market hattındasın. Erzak ve ilaç kararları ekibin yönünü belirliyor.",
        "s01c01_scene15_chapter_finale" or "s01c01_scene16_ending_narration" => "Kuzey yolu göründü. Otogar ve araç rotası bir sonraki bölümün ana izi.",
        _ => "Rota durumu mevcut sahneye göre güncelleniyor."
    };
    public IReadOnlyList<MapTimelineItem> MapTimeline => CreateMapTimeline();
    public bool HasSceneHint => !string.IsNullOrWhiteSpace(SceneHint);
    public bool HasVoiceMessage => SceneVoiceMessage is not null;
    public bool CanPlayVoiceMessage => SceneVoiceMessage is { AudioPath.Length: > 0 } message &&
                                       _audio.HasAudioFile(message.AudioPath) &&
                                       (!IsVoiceChoiceGateScene || IsVoiceChoiceConfirmed) &&
                                       !IsVoiceMessageBlocked;
    public bool IsVoiceChoiceGateScene => Scene?.Id == "s01c01_scene01_wakeup";
    public bool AreChoicesEnabled => !IsVoiceChoiceGateScene || !IsVoiceChoiceConfirmed;
    public bool CanConfirmVoiceChoice => IsVoiceChoiceGateScene && HasSelectedChoice && !IsVoiceChoiceConfirmed;
    public bool ShowVoiceChoiceConfirm => IsVoiceChoiceGateScene;
    public string VoiceMessageTitle => SceneVoiceMessage?.Title ?? "";
    public string VoiceMessageTranscript => SceneVoiceMessage?.Transcript ?? "";
    public string VoiceMessageHint => IsVoiceChoiceGateScene
        ? IsVoiceChoiceConfirmed
            ? IsVoiceMessageBlocked ? "Telefon şarjı korundu; bu kayıt bu sahnede kapalı." : "Karar kilitlendi. Kayıt artık dinlenebilir."
            : "Önce bir seçenek seçip Seç tuşuyla kararı kilitle."
        : "Kayıt hazır. Dinleyebilir veya duraklatabilirsin.";
    public string VoiceMessageStateText => SceneVoiceMessage is null
        ? ""
        : IsVoiceChoiceGateScene && !IsVoiceChoiceConfirmed
            ? "Önce bir karar seç"
        : IsVoiceMessageBlocked
            ? "Telefon şarjı korundu, kayıt dinlenemez"
        : State.ListenedVoiceMessages.Contains(SceneVoiceMessage.Id)
            ? "Dinlendi"
            : CanPlayVoiceMessage ? "Dinlenmedi" : "Ses dosyası yok, transcript açık";
    private bool IsVoiceMessageBlocked =>
        Scene?.Id == "s01c01_scene01_wakeup" &&
        IsVoiceChoiceConfirmed &&
        (SelectedChoice?.Id == "s01c01_choice_save_battery" ||
         State.Flags.Contains("chapter1.deryaMessageNotFullyHeard"));

    partial void OnIsVoiceChoiceConfirmedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlayVoiceMessage));
        OnPropertyChanged(nameof(CanConfirmVoiceChoice));
        OnPropertyChanged(nameof(AreChoicesEnabled));
        OnPropertyChanged(nameof(VoiceMessageHint));
        OnPropertyChanged(nameof(VoiceMessageStateText));
        ConfirmVoiceChoiceCommand.NotifyCanExecuteChanged();
        AdvanceSelectedChoiceCommand.NotifyCanExecuteChanged();
        PlayVoiceMessageCommand.NotifyCanExecuteChanged();
        PauseVoiceMessageCommand.NotifyCanExecuteChanged();
        RestartVoiceMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnSceneHintChanged(string value) => OnPropertyChanged(nameof(HasSceneHint));
    partial void OnSceneVoiceMessageChanged(VoiceMessageDefinition? value)
    {
        OnPropertyChanged(nameof(HasVoiceMessage));
        OnPropertyChanged(nameof(CanPlayVoiceMessage));
        OnPropertyChanged(nameof(VoiceMessageTitle));
        OnPropertyChanged(nameof(VoiceMessageTranscript));
        OnPropertyChanged(nameof(VoiceMessageHint));
        OnPropertyChanged(nameof(VoiceMessageStateText));
        OnPropertyChanged(nameof(IsVoiceChoiceGateScene));
        OnPropertyChanged(nameof(ShowVoiceChoiceConfirm));
        OnPropertyChanged(nameof(CanConfirmVoiceChoice));
        OnPropertyChanged(nameof(AreChoicesEnabled));
        PlayVoiceMessageCommand.NotifyCanExecuteChanged();
        PauseVoiceMessageCommand.NotifyCanExecuteChanged();
        RestartVoiceMessageCommand.NotifyCanExecuteChanged();
        ConfirmVoiceChoiceCommand.NotifyCanExecuteChanged();
    }

    partial void OnSceneChanged(StoryScene? value)
    {
        OnPropertyChanged(nameof(IsChapterEndingScene));
        OnPropertyChanged(nameof(ScenarioTasks));
    }

    partial void OnStatusTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsCharacterTab));
        OnPropertyChanged(nameof(IsTeamTab));
        OnPropertyChanged(nameof(IsInventoryTab));
        OnPropertyChanged(nameof(IsMapTab));
        OnPropertyChanged(nameof(IsTasksTab));
        OnPropertyChanged(nameof(IsHistoryTab));
    }

    partial void OnSelectedChoiceChanged(ChoiceViewModel? value)
    {
        BuildHelpOptions(value);
        OnPropertyChanged(nameof(HasSelectedChoice));
        OnPropertyChanged(nameof(SelectedChoiceImage));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(SelectionRisk));
        OnPropertyChanged(nameof(SelectionRequirement));
        OnPropertyChanged(nameof(VoiceMessageStateText));
        OnPropertyChanged(nameof(VoiceMessageHint));
        OnPropertyChanged(nameof(CanConfirmVoiceChoice));
        OnPropertyChanged(nameof(CanPlayVoiceMessage));
        ConfirmVoiceChoiceCommand.NotifyCanExecuteChanged();
        AdvanceSelectedChoiceCommand.NotifyCanExecuteChanged();
        PlayVoiceMessageCommand.NotifyCanExecuteChanged();
        PauseVoiceMessageCommand.NotifyCanExecuteChanged();
        RestartVoiceMessageCommand.NotifyCanExecuteChanged();
    }

    public void Start()
    {
        State.SessionInProgress = true;
        RefreshScene();
        StartClock();
        StartPeriodicSave();
    }

    [RelayCommand]
    private async Task ApplyChoiceAsync(ChoiceViewModel choice)
    {
        if (!choice.IsAvailable)
        {
            return;
        }
        _timerCancellation?.Cancel();
        var previousSceneTitle = Scene?.Title ?? "Bilinmeyen sahne";
        var previousLocation = State.Location;
        var previousInventory = State.Inventory.ToDictionary(item => item.Id, item => item.Quantity, StringComparer.OrdinalIgnoreCase);
        var blockedVoiceWithChoice = Scene?.Id == "s01c01_scene01_wakeup" && choice.Id == "s01c01_choice_save_battery";
        _engine.Choose(State, choice.Id);
        var inventoryGains = FindInventoryGains(previousInventory).ToList();
        var discoveries = FindChoiceDiscoveries(choice.OriginalChoice).ToList();
        if (_settings.AutoSave)
        {
            await _saveService.SaveAsync(1, State);
            SaveStatus = "Otomatik kaydedildi";
        }
        if (State.Ending != EndingKind.None)
        {
            State.SessionInProgress = false;
            await _saveService.SaveAsync(1, State);
            NotifyStateChanged();
            _onEnded();
            return;
        }
        RefreshScene();
        if (blockedVoiceWithChoice)
        {
            QueueTopNotification(new TopMessageViewModel(
                "",
                "Ses Kaydı",
                "Telefonunu korudun; Derya'nın kaydını şimdi dinleyemezsin.",
                "Uyarı",
                "DiscoveryFound",
                "!",
                "Şarj ileride işe yarayabilir ama bu kayıt atlandı.",
                "",
                "Karar kilitlendi"));
        }
        QueueInventoryGainNotifications(inventoryGains, previousSceneTitle, previousLocation, choice.Text);
        QueueDiscoveryNotifications(discoveries, previousSceneTitle, previousLocation, choice.Text);
        if (_settings.AutoSave && (inventoryGains.Count > 0 || discoveries.Count > 0))
        {
            await _saveService.SaveAsync(1, State);
        }
    }

    [RelayCommand]
    private void SelectChoice(ChoiceViewModel choice)
    {
        if (!AreChoicesEnabled)
        {
            return;
        }

        foreach (var candidate in Choices)
        {
            candidate.IsSelected = ReferenceEquals(candidate, choice);
        }
        SelectedChoice = choice;
        _ = SaveEmergencySnapshotAsync();
    }

    [RelayCommand(CanExecute = nameof(CanConfirmVoiceChoice))]
    private void ConfirmVoiceChoice()
    {
        if (SelectedChoice is null || !IsVoiceChoiceGateScene)
        {
            return;
        }

        IsVoiceChoiceConfirmed = true;
        if (SelectedChoice.Id == "s01c01_choice_save_battery")
        {
            QueueTopNotification(new TopMessageViewModel(
                "",
                "Ses Kaydı",
                "Telefonunu korudun; Derya'nın kaydını şimdi dinleyemezsin.",
                "Uyarı",
                "DiscoveryFound",
                "!",
                "Şarj ileride işe yarayabilir ama bu kayıt bu sahnede kapandı.",
                "",
                "Karar kilitlendi"));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdvanceSelectedChoice))]
    private async Task AdvanceSelectedChoiceAsync()
    {
        if (SelectedChoice is { IsAvailable: true } choice)
        {
            await ApplyChoiceAsync(choice);
        }
    }

    private bool CanAdvanceSelectedChoice() =>
        SelectedChoice is { IsAvailable: true } &&
        (!IsVoiceChoiceGateScene || IsVoiceChoiceConfirmed);

    [RelayCommand]
    private void Undo()
    {
        if (_engine.Undo(State))
        {
            SaveStatus = "Son seçim geri alındı";
            RefreshScene();
        }
    }

    [RelayCommand]
    private void ShowInstantly()
    {
        _typingCancellation?.Cancel();
        DisplayedNarrative = _fullNarrative;
    }

    [RelayCommand(CanExecute = nameof(CanPlayVoiceMessage))]
    private void PlayVoiceMessage()
    {
        if (SceneVoiceMessage is null)
        {
            return;
        }

        if (IsVoiceMessageBlocked)
        {
            QueueTopNotification(new TopMessageViewModel(
                "",
                "Ses Kaydı",
                "Telefonunu korudun; bu kayıt artık dinlenemez.",
                "Uyarı",
                "DiscoveryFound",
                "!",
                "Bu karar geri alınmadan kayıt açılamaz.",
                "",
                "Şarj korundu"));
            return;
        }

        _audio.PlayVoice(SceneVoiceMessage.AudioPath, SceneVoiceMessage.VolumeMultiplier);
        IsVoiceMessagePlaying = true;
        MarkVoiceMessageListened(SceneVoiceMessage);
        UpdateVoiceMessageTime();
        StartVoiceProgressLoop();
    }

    [RelayCommand(CanExecute = nameof(CanPlayVoiceMessage))]
    private void PauseVoiceMessage()
    {
        _audio.PauseVoice();
        IsVoiceMessagePlaying = false;
        UpdateVoiceMessageTime();
        StopVoiceProgressLoop();
    }

    [RelayCommand(CanExecute = nameof(CanPlayVoiceMessage))]
    private void RestartVoiceMessage()
    {
        _audio.RestartVoice();
        IsVoiceMessagePlaying = true;
        if (SceneVoiceMessage is not null)
        {
            MarkVoiceMessageListened(SceneVoiceMessage);
        }
        UpdateVoiceMessageTime();
        StartVoiceProgressLoop();
    }

    [RelayCommand]
    private void ReturnFromChapterEnding()
    {
        Stop();
        _onExit();
    }

    [RelayCommand]
    private void ToggleVoiceTranscript() => IsVoiceTranscriptVisible = !IsVoiceTranscriptVisible;

    [RelayCommand]
    private void ToggleStatusPanel() => IsStatusPanelOpen = !IsStatusPanelOpen;

    [RelayCommand]
    private void SelectStatusTab(string tab) => StatusTab = tab;

    [RelayCommand]
    private async Task OpenSavePanelAsync()
    {
        await RefreshSaveSlotsAsync();
        IsConfirmingOverwrite = false;
        SaveProgress = 0;
        SaveProgressText = "Kayıt yuvası seç";
        IsSavePanelOpen = true;
    }

    [RelayCommand]
    private async Task RequestSaveSlotAsync(int slot)
    {
        if (IsSaving)
        {
            return;
        }

        PendingSaveSlot = slot;
        var selected = SaveSlots.FirstOrDefault(saveSlot => saveSlot.Slot == slot);
        if (selected is not null && !selected.IsEmpty)
        {
            IsConfirmingOverwrite = true;
            SaveProgressText = $"{slot}. yuvadaki kayıt değiştirilecek.";
            return;
        }

        await SaveToSlotAsync(slot);
    }

    [RelayCommand]
    private async Task ConfirmOverwriteAsync()
    {
        if (PendingSaveSlot > 0)
        {
            await SaveToSlotAsync(PendingSaveSlot);
        }
    }

    [RelayCommand]
    private void CancelOverwrite()
    {
        IsConfirmingOverwrite = false;
        SaveProgressText = "Kayıt yuvası seç";
    }

    [RelayCommand]
    private void CloseSavePanel()
    {
        if (!IsSaving)
        {
            IsSavePanelOpen = false;
            IsConfirmingOverwrite = false;
        }
    }

    [RelayCommand]
    private async Task ExitToMenuAsync()
    {
        State.SessionInProgress = false;
        await _saveService.SaveAsync(1, State);
        Stop();
        _onExit();
    }

    public void Stop()
    {
        _timerCancellation?.Cancel();
        _typingCancellation?.Cancel();
        _clockCancellation?.Cancel();
        _topMessageCancellation?.Cancel();
        _periodicSaveCancellation?.Cancel();
        StopVoiceProgressLoop();
        _topNotificationQueue.Clear();
        _isTopNotificationWorkerRunning = false;
    }

    public async Task SaveEmergencySnapshotAsync()
    {
        if (State.Ending == EndingKind.None)
        {
            await _saveService.SaveAsync(1, State);
            SaveStatus = "İlerleme kaydedildi";
        }
    }

    public bool RestoreCheckpoint()
    {
        var restored = _engine.RestoreCheckpoint(State);
        if (restored)
        {
            RefreshScene();
        }
        return restored;
    }

    private void RefreshScene()
    {
        Scene = _engine.CurrentScene(State);
        var firstVisit = State.VisitedScenes.Add(Scene.Id);
        _audio.StopVoice();
        IsVoiceMessagePlaying = false;
        StopVoiceProgressLoop();
        SceneVoiceMessage = Scene.VoiceMessage;
        IsVoiceChoiceConfirmed = false;
        IsVoiceTranscriptVisible = SceneVoiceMessage is not null && !CanPlayVoiceMessage;
        VoiceMessageTimeText = "00:00 / --:--";
        VoiceProgress = 0;
        ActiveSceneImage = string.IsNullOrWhiteSpace(Scene.SceneImage) ? "images/scenes/fallback.webp" : Scene.SceneImage;
        SelectedChoice = null;
        Choices.Clear();
        foreach (var availability in _engine.GetChoices(State))
        {
            Choices.Add(new ChoiceViewModel(availability));
        }

        var narration = Scene.Content.Where(block => block.Type.Equals("narration", StringComparison.OrdinalIgnoreCase)).Select(block => block.Text);
        _fullNarrative = string.Join(Environment.NewLine + Environment.NewLine, narration);
        var dialogue = Scene.Content.LastOrDefault(block => block.Type.Equals("dialogue", StringComparison.OrdinalIgnoreCase));
        Speaker = dialogue?.Speaker ?? "";
        Dialogue = dialogue?.Text ?? "";
        DialogueSpeakerId = ResolveCharacterId(Speaker);
        _audio.EnterScene(Scene);
        ShakeIntensity = Math.Clamp(Scene.ScreenShakeIntensity, 0, 12);
        if (ShakeIntensity > 0)
        {
            ShakeRequestToken++;
        }
        NoiseOpacity = Math.Clamp(Scene.NoiseIntensity, 0, 0.16);
        IsNoiseEffectVisible = NoiseOpacity > 0;
        SceneHint = CreateInventorySuggestion(firstVisit);
        QueueSceneMessages(firstVisit, dialogue);
        QueueTeamReaction(firstVisit);
        QueueVoiceMessage(firstVisit);
        if (firstVisit && HasSceneHint)
        {
            AddSystemMessage(SceneHint, "Envanter", "Sahne önerisi");
        }
        StartTypewriter();
        StartTimer();
        StartTopMessageQueue();
        NotifyStateChanged();
        OnPropertyChanged(nameof(UsableInventory));
        OnPropertyChanged(nameof(MapCurrentX));
        OnPropertyChanged(nameof(MapCurrentY));
        OnPropertyChanged(nameof(MapLocationLabel));
        OnPropertyChanged(nameof(MapStatus));
        OnPropertyChanged(nameof(MapTimeline));
        OnPropertyChanged(nameof(ScenarioTasks));
        _ = SaveEmergencySnapshotAsync();
    }

    private void QueueVoiceMessage(bool firstVisit)
    {
        if (!firstVisit || SceneVoiceMessage is null)
        {
            return;
        }

        if (SceneVoiceMessage.AddToMessageHistory)
        {
            var speaker = string.IsNullOrWhiteSpace(SceneVoiceMessage.SpeakerId) ? PlayerDefinition.Name : SceneVoiceMessage.SpeakerId;
            AddMessage(speaker, SceneVoiceMessage.Title, "Ses Kaydı");
        }

        if (SceneVoiceMessage.AutoPlay && CanPlayVoiceMessage)
        {
            PlayVoiceMessage();
        }
    }

    private void MarkVoiceMessageListened(VoiceMessageDefinition message)
    {
        if (message.RememberAsListened && State.ListenedVoiceMessages.Add(message.Id))
        {
            OnPropertyChanged(nameof(VoiceMessageStateText));
            _ = SaveEmergencySnapshotAsync();
        }
    }

    private void UpdateVoiceMessageTime()
    {
        var elapsed = _audio.VoicePosition;
        var duration = _audio.VoiceDuration;
        VoiceMessageTimeText = duration is null
            ? $"{elapsed:mm\\:ss} / --:--"
            : $"{elapsed:mm\\:ss} / {duration.Value:mm\\:ss}";
        VoiceProgress = duration is { TotalMilliseconds: > 0 }
            ? (int)Math.Clamp(elapsed.TotalMilliseconds / duration.Value.TotalMilliseconds * 100, 0, 100)
            : 0;
    }

    private async void StartVoiceProgressLoop()
    {
        _voiceProgressCancellation?.Cancel();
        _voiceProgressCancellation = new CancellationTokenSource();
        var token = _voiceProgressCancellation.Token;
        try
        {
            while (!token.IsCancellationRequested && IsVoiceMessagePlaying)
            {
                UpdateVoiceMessageTime();
                await Task.Delay(250, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopVoiceProgressLoop()
    {
        _voiceProgressCancellation?.Cancel();
        _voiceProgressCancellation = null;
    }

    private async void StartTypewriter()
    {
        _typingCancellation?.Cancel();
        _typingCancellation = new CancellationTokenSource();
        var mode = _settings.TextAnimation ? _settings.TextAnimationMode : "Anında";
        if (mode == "Anında" || _settings.TextSpeed == "Anında")
        {
            DisplayedNarrative = _fullNarrative;
            return;
        }

        var delay = _settings.TextSpeed switch { "Yavaş" => 34, "Hızlı" => 10, _ => 20 };
        try
        {
            if (mode == "Yumuşak Belirme")
            {
                DisplayedNarrative = _fullNarrative;
                NarrativeFadeToken++;
                return;
            }

            DisplayedNarrative = "";
            if (mode == "Satır Satır")
            {
                foreach (var line in _fullNarrative.Split(Environment.NewLine))
                {
                    _typingCancellation.Token.ThrowIfCancellationRequested();
                    DisplayedNarrative += (DisplayedNarrative.Length == 0 ? "" : Environment.NewLine) + line;
                    await Task.Delay(delay * 12, _typingCancellation.Token);
                }
                return;
            }

            if (mode == "Kelime Kelime")
            {
                foreach (var word in _fullNarrative.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    _typingCancellation.Token.ThrowIfCancellationRequested();
                    DisplayedNarrative += (DisplayedNarrative.Length == 0 ? "" : " ") + word;
                    await Task.Delay(delay * 3, _typingCancellation.Token);
                }
                return;
            }

            foreach (var character in _fullNarrative)
            {
                _typingCancellation.Token.ThrowIfCancellationRequested();
                DisplayedNarrative += character;
                await Task.Delay(delay, _typingCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            DisplayedNarrative = _fullNarrative;
        }
    }

    private async void StartTimer()
    {
        _timerCancellation?.Cancel();
        if (Scene?.TimedChoiceSeconds is not int seconds || seconds <= 0)
        {
            IsTimedChoice = false;
            return;
        }

        _timerCancellation = new CancellationTokenSource();
        IsTimedChoice = true;
        CountdownMaximum = seconds;
        CountdownSeconds = seconds;
        try
        {
            while (CountdownSeconds > 0)
            {
                await Task.Delay(1000, _timerCancellation.Token);
                CountdownSeconds--;
            }
            var defaultChoice = Choices.FirstOrDefault(choice => choice.Id == Scene.DefaultChoiceId && choice.IsAvailable);
            if (defaultChoice is not null)
            {
                await ApplyChoiceAsync(defaultChoice);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void StartTopMessageQueue()
    {
        if (!_settings.TopMessageNotifications || Scene is null)
        {
            return;
        }

        var queued = State.Messages.Where(message => message.SceneId == Scene.Id && !message.IsRead).ToList();
        foreach (var message in queued)
        {
            var flag = $"top_message_shown_{message.Id}";
            if (!State.Flags.Add(flag))
            {
                continue;
            }

            QueueTopNotification(new TopMessageViewModel(
                message.CharacterId,
                message.CharacterName,
                message.Text,
                message.Tone,
                "CharacterMessage",
                CharacterInitial(message.CharacterName),
                message.RelationshipContext,
                "",
                "Ekip haberleşmesi"));
        }
        EnsureTopNotificationWorker();
    }

    private void QueueTopNotification(TopMessageViewModel notification)
    {
        if (!_settings.TopMessageNotifications)
        {
            return;
        }

        _topNotificationQueue.Enqueue(notification);
        EnsureTopNotificationWorker();
    }

    private async void EnsureTopNotificationWorker()
    {
        if (_isTopNotificationWorkerRunning)
        {
            return;
        }

        _topMessageCancellation?.Cancel();
        _topMessageCancellation = new CancellationTokenSource();
        var token = _topMessageCancellation.Token;
        _isTopNotificationWorkerRunning = true;
        try
        {
            while (_topNotificationQueue.Count > 0)
            {
                var notification = _topNotificationQueue.Dequeue();
                VisibleTopMessages.Clear();
                VisibleTopMessages.Add(notification);
                _audio.PlayNotification(notification.Type == "DangerWarning" || notification.Tone == "Tehlike");
                await Task.Delay(2800, token);
                VisibleTopMessages.Clear();
                await Task.Delay(260, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isTopNotificationWorkerRunning = false;
        }
    }

    private void QueueSceneMessages(bool firstVisit, StoryBlock? dialogue)
    {
        if (!firstVisit || Scene is null)
        {
            return;
        }

        foreach (var message in Scene.TopMessages)
        {
            AddMessage(message.Speaker, message.Text, message.Tone);
        }
        if (dialogue is not null &&
            !Scene.TopMessages.Any(message => message.Speaker.Equals(dialogue.Speaker, StringComparison.OrdinalIgnoreCase) &&
                                              message.Text.Equals(dialogue.Text, StringComparison.Ordinal)))
        {
            AddMessage(dialogue.Speaker, dialogue.Text, "Konuşma");
        }
    }

    private void QueueTeamReaction(bool firstVisit)
    {
        if (!firstVisit || Scene is null)
        {
            return;
        }

        var speakerId = Scene.Id switch
        {
            "s01c01_scene05_exit_together" or "s01c01_scene06_silent_istanbul" or "s01c01_scene10_apartment_fall" => "hakan",
            "s01c01_scene07_first_shelter" or "s01c01_scene08_power_antenna" or "s01c01_scene09_derya_recording" => "yesking",
            "s01c01_scene11_bedo_arrives" or "s01c01_scene12_first_night_team" or "s01c01_scene13_first_supply_run" => "bedo",
            "s01c01_scene14_meet_busra" or "s01c01_scene15_chapter_finale" => "busra",
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(speakerId) || !IsCharacterKnown(speakerId))
        {
            return;
        }

        var flag = $"team_message_{Scene.Id}_{speakerId}";
        if (!State.Flags.Add(flag))
        {
            return;
        }

        var speaker = CharacterDefinitions.GetValueOrDefault(speakerId)?.Name ?? speakerId;
        AddMessage(speaker, "", Scene.Id.Contains("finale", StringComparison.OrdinalIgnoreCase) ? "Uyarı" : "Bilgi");
        StartTopMessageQueue();
    }

    private string CreateInventorySuggestion(bool firstVisit)
    {
        if (!firstVisit || Scene is null)
        {
            return "";
        }

        ItemState? item = Scene.Id switch
        {
            "apartment_01" or "service_door" => State.Inventory.FirstOrDefault(candidate =>
                candidate.IsUsable && candidate.Id is "crowbar" or "wrench" or "screwdriver" or "pliers"),
            "radio_room" => State.Inventory.FirstOrDefault(candidate =>
                candidate.IsUsable && candidate.Id is "fresh_battery" or "battery" or "wire" or "screwdriver"),
            "bedos_fall" or "tunnel_bite" => State.Inventory.FirstOrDefault(candidate =>
                candidate.IsUsable && candidate.Id is "bandage" or "medkit" or "antiseptic"),
            _ when State.Thirst >= 60 => State.Inventory.FirstOrDefault(candidate => candidate.IsUsable && candidate.Category == ItemCategory.Drink),
            _ when State.Hunger >= 60 => State.Inventory.FirstOrDefault(candidate => candidate.IsUsable && candidate.Category == ItemCategory.Food),
            _ when State.Health <= 55 => State.Inventory.FirstOrDefault(candidate => candidate.IsUsable && candidate.Category == ItemCategory.Health),
            _ => null
        };
        if (item is null)
        {
            return "";
        }

        var flag = $"inventory_hint_{Scene.Id}_{item.Id}";
        if (!State.Flags.Add(flag))
        {
            return "";
        }
        return $"{item.Name} burada işine yarayabilir. Envanter panelinden kullanabilirsin.";
    }

    private void AddSystemMessage(string text, string tone, string context)
    {
        State.Messages.Add(new CharacterMessageState
        {
            CharacterId = PlayerDefinition.Id,
            CharacterName = PlayerDefinition.Name,
            Text = text,
            Tone = tone,
            RelationshipContext = context,
            Day = State.Day,
            GameTime = TimeText,
            SceneId = Scene?.Id ?? ""
        });
        OnPropertyChanged(nameof(MessageHistory));
        OnPropertyChanged(nameof(UnreadMessageCount));
    }

    private IEnumerable<InventoryGain> FindInventoryGains(IReadOnlyDictionary<string, int> previousInventory)
    {
        foreach (var item in State.Inventory)
        {
            var previousQuantity = previousInventory.GetValueOrDefault(item.Id);
            var gained = item.Quantity - previousQuantity;
            if (gained > 0)
            {
                yield return new InventoryGain(item, gained);
            }
        }
    }

    private IEnumerable<TopMessageViewModel> FindChoiceDiscoveries(StoryChoice choice)
    {
        foreach (var effect in choice.Effects)
        {
            if (effect.Type.Equals("UnlockRoute", StringComparison.OrdinalIgnoreCase))
            {
                var route = string.IsNullOrWhiteSpace(effect.Value) ? "Yeni rota" : effect.Value;
                yield return CreateDiscoveryNotification(
                    "Yeni rota bilgisi açıldı.",
                    $"{route} haritana işlendi.",
                    "Rota");
            }
            else if (effect.Type.Equals("AddTask", StringComparison.OrdinalIgnoreCase) ||
                     effect.Type.Equals("CompleteTask", StringComparison.OrdinalIgnoreCase))
            {
                yield return new TopMessageViewModel(
                    "",
                    "Görev",
                    string.IsNullOrWhiteSpace(effect.Value) ? "Görev güncellendi." : effect.Value,
                    "Görev",
                    "QuestUpdated",
                    "◆",
                    "Görev güncellendi",
                    "",
                    "Durum kaydedildi");
            }
            else if (effect.Type.Equals("SetFlag", StringComparison.OrdinalIgnoreCase) &&
                     IsDiscoveryFlag(effect.Value))
            {
                yield return CreateDiscoveryNotification(
                    DiscoveryTitle(effect.Value),
                    DiscoveryDescription(effect.Value),
                    "Keşif");
            }
        }
    }

    private void QueueInventoryGainNotifications(
        IReadOnlyList<InventoryGain> gains,
        string sceneTitle,
        string location,
        string choiceText)
    {
        foreach (var gain in gains)
        {
            QueueTopNotification(new TopMessageViewModel(
                "",
                "Envanter",
                $"{gain.Item.Name} x{gain.Amount} eklendi.",
                InventoryTone(gain.Item),
                "InventoryAdded",
                InventoryIcon(gain.Item),
                string.IsNullOrWhiteSpace(gain.Item.Description) ? CategoryDescription(gain.Item) : gain.Item.Description,
                $"{new InventoryDisplay(gain.Item).Category} • x{gain.Amount}",
                "Envantere eklendi"));

            State.Journal.Add(new JournalEntry
            {
                Text = $"{sceneTitle}: {CleanChoiceText(choiceText)} ve {gain.Item.Name} x{gain.Amount} envantere eklendi.",
                Category = "Envanter",
                Location = location,
                Day = State.Day,
                GameTime = TimeText
            });
        }

        if (gains.Count > 0)
        {
            NotifyStateChanged();
        }
    }

    private void QueueDiscoveryNotifications(
        IReadOnlyList<TopMessageViewModel> discoveries,
        string sceneTitle,
        string location,
        string choiceText)
    {
        foreach (var discovery in discoveries)
        {
            QueueTopNotification(discovery);
            State.Journal.Add(new JournalEntry
            {
                Text = $"{sceneTitle}: {CleanChoiceText(choiceText)}. {discovery.Text}",
                Category = "Keşif",
                Location = location,
                Day = State.Day,
                GameTime = TimeText
            });
        }

        if (discoveries.Count > 0)
        {
            NotifyStateChanged();
        }
    }

    private static TopMessageViewModel CreateDiscoveryNotification(string title, string description, string amountText) =>
        new("", "Keşif", title, "Keşif", "DiscoveryFound", "⌖", description, amountText, "Geçmişe işlendi");

    private static bool IsDiscoveryFlag(string value) =>
        value.Contains("derya", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("map.", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("route", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("kirmizi", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("red", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("threatKnowledge", StringComparison.OrdinalIgnoreCase);

    private static string DiscoveryTitle(string flag) => flag switch
    {
        var value when value.Contains("derya", StringComparison.OrdinalIgnoreCase) => "Derya'ya ait olabilecek bir iz buldun.",
        var value when value.Contains("map.", StringComparison.OrdinalIgnoreCase) => "Haritana yeni bilgi işlendi.",
        var value when value.Contains("phone", StringComparison.OrdinalIgnoreCase) => "Telefon kaydı geçmişe eklendi.",
        var value when value.Contains("threatKnowledge", StringComparison.OrdinalIgnoreCase) => "Tehlike hakkında yeni ipucu öğrendin.",
        _ => "Önemli bir keşif yaptın."
    };

    private static string DiscoveryDescription(string flag) => flag switch
    {
        var value when value.Contains("derya", StringComparison.OrdinalIgnoreCase) => "Bu iz Derya'nın rotasına yaklaşmanı sağlayabilir.",
        var value when value.Contains("map.", StringComparison.OrdinalIgnoreCase) => "Yeni rota bilgisi ileride seçenek açabilir.",
        var value when value.Contains("phone", StringComparison.OrdinalIgnoreCase) => "Kayıt ve şarj bilgisi ileride önemli olabilir.",
        var value when value.Contains("threatKnowledge", StringComparison.OrdinalIgnoreCase) => "Enfektelerin davranışını anlamak hayatta kalmanı kolaylaştırır.",
        _ => "Bu bilgi ileride kararlarını etkileyebilir."
    };

    private static string InventoryIcon(ItemState item) => item.Category switch
    {
        ItemCategory.Food => "▣",
        ItemCategory.Drink => "◌",
        ItemCategory.Health => "✚",
        ItemCategory.Tool => "⌘",
        ItemCategory.Weapon => "†",
        ItemCategory.Quest => "⌖",
        _ => "▥"
    };

    private static string InventoryTone(ItemState item) =>
        item.Category is ItemCategory.Weapon ? "Tehlike" :
        item.Category is ItemCategory.Quest ? "Keşif" :
        "Envanter";

    private static string CategoryDescription(ItemState item) => item.Category switch
    {
        ItemCategory.Food => "Tokluğu biraz artırır.",
        ItemCategory.Drink => "Susuzluğu azaltır.",
        ItemCategory.Health => "Yaralanmalarda kullanılabilir.",
        ItemCategory.Tool => "Bazı kilit, elektrik veya cihaz seçeneklerini açabilir.",
        ItemCategory.Weapon => "Yakın tehlikelerde kullanılabilir.",
        ItemCategory.Quest => "Bu eşya ileride önemli olabilir.",
        _ => "Kaynak olarak saklandı."
    };

    private static string CleanChoiceText(string text) =>
        text.Replace("➦", "", StringComparison.Ordinal)
            .Replace("⚠", "", StringComparison.Ordinal)
            .Replace("⛓", "", StringComparison.Ordinal)
            .Trim();

    private static string CharacterInitial(string name) =>
        string.IsNullOrWhiteSpace(name) ? "!" : name.Trim()[0].ToString().ToUpperInvariant();

    private void AddMessage(string speaker, string originalText, string tone)
    {
        var characterId = ResolveCharacterId(speaker);
        if (string.IsNullOrWhiteSpace(characterId) || !characterId.Equals(PlayerDefinition.Id, StringComparison.OrdinalIgnoreCase) && !IsCharacterKnown(characterId))
        {
            return;
        }

        var relationship = State.Relationships.FirstOrDefault(item => item.CharacterId.Equals(characterId, StringComparison.OrdinalIgnoreCase));
        var trust = relationship?.Trust ?? 50;
        var text = string.IsNullOrWhiteSpace(originalText)
            ? ComposeCharacterMessage(characterId, trust)
            : originalText;
        var recent = State.Messages.TakeLast(10).ToList();
        if (recent.Any(message => message.Text.Equals(text, StringComparison.OrdinalIgnoreCase)) ||
            recent.TakeLast(2).Count(message => message.CharacterId.Equals(characterId, StringComparison.OrdinalIgnoreCase)) == 2)
        {
            return;
        }
        State.Messages.Add(new CharacterMessageState
        {
            CharacterId = characterId,
            CharacterName = speaker,
            Text = text,
            Tone = tone,
            RelationshipContext = relationship?.Status ?? "Temkinli",
            Day = State.Day,
            GameTime = TimeText,
            SceneId = Scene?.Id ?? ""
        });
        OnPropertyChanged(nameof(MessageHistory));
        OnPropertyChanged(nameof(UnreadMessageCount));
    }

    private bool IsCharacterKnown(string characterId)
    {
        if (characterId.Equals(State.SelectedCharacterId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return characterId switch
        {
            "hakan" => State.Flags.Contains("party.hakanPresent") || State.Flags.Contains("hakan.status.withPlayer") || State.Flags.Contains("hakan.status.rescued"),
            "yesking" => State.Flags.Contains("yesking.joined") || State.Flags.Contains("party.yeskingPresent"),
            "bedo" => State.Flags.Contains("bedo.joined") || State.Flags.Contains("bedo.joinedReluctant") || State.Flags.Contains("party.bedoPresent"),
            "busra" => State.Flags.Contains("busra.joinedLater") || State.Flags.Contains("busra.joinCandidate"),
            _ => false
        };
    }

    private IReadOnlyList<ScenarioTaskDisplay> CreateScenarioTasks()
    {
        var groups = new[]
        {
            new ScenarioTaskGroup(1, 2, "Sağlık merkezinden çık", "Telefonu ve ilk kaynakları kontrol et|Koridordaki tehdidi aş"),
            new ScenarioTaskGroup(3, 3, "İlk enfekte karşılaşmasını atlat", "Gürültüyü kontrol altında tut|Güvenli çıkışı belirle"),
            new ScenarioTaskGroup(4, 5, "Hakan hakkında karar ver", "Yarasını değerlendir|Yanına al, söz ver veya geride bırak"),
            new ScenarioTaskGroup(6, 6, "Sessiz İstanbul'da iz bul", "Kırmızı işaretleri takip et|Güvenli sığınak ara"),
            new ScenarioTaskGroup(7, 9, "Telsiz bağlantısını kur", "Yesking ile güven oluştur|Anteni çalıştır|Derya'nın kaydını çöz"),
            new ScenarioTaskGroup(10, 11, "Çatı hattından sağ çık", "Düşüşün sonuçlarını yönet|Bedo ile karşılaş"),
            new ScenarioTaskGroup(12, 14, "İlk gece için erzak topla", "Market rotasını seç|Büşra ve yaralı için ilaç kararı ver"),
            new ScenarioTaskGroup(15, 16, "Kuzey yoluna hazırlan", "Son rotayı belirle|Bölüm sonu kaydını dinle")
        };
        var current = SceneIndex();
        var result = new List<ScenarioTaskDisplay>();
        foreach (var group in groups)
        {
            var status = current > group.End ? "Tamamlandı" : current >= group.Start ? "Aktif" : "Gizli";
            if (status == "Gizli" && group.Start > current + 2)
            {
                continue;
            }
            var progress = status == "Tamamlandı" ? 100 : status == "Aktif"
                ? Math.Clamp((current - group.Start + 1) * 100 / (group.End - group.Start + 1), 15, 95)
                : 0;
            result.Add(new ScenarioTaskDisplay(group.Title, "Ana görev", status, progress, "Hikâye ilerledikçe alt hedefler açılır."));
            foreach (var subtask in group.Subtasks.Split('|'))
            {
                result.Add(new ScenarioTaskDisplay(subtask, "Alt görev", status, progress, ""));
            }
        }
        return result;
    }

    private string TeamLastEvent(CharacterState character)
    {
        if (character.HiddenInfoUnlocked)
        {
            return "Sana geçmişinden bir parça anlattı.";
        }

        return character.Id switch
        {
            "hakan" when State.Flags.Contains("hakan.status.rescued") => "Verdiğin sözü tutup onu geri aldın.",
            "hakan" => "Yarası hareketini yavaşlatıyor; çevreyi polis dikkatiyle izliyor.",
            "yesking" when State.Flags.Contains("party.yeskingPresent") => "Apartmandaki telsiz işiyle ekibe bağlandı.",
            "yesking" => "Sığınağın üst katında temkinli şekilde yanında duruyor.",
            "bedo" when State.Flags.Contains("party.bedoPresent") => "Çatı hattında sana yetişti; hızlı ama mesafeli.",
            "bedo" => "Seni izledi, tarttı, şimdilik aynı yöne gidiyor.",
            "busra" when State.Flags.Contains("busra.joinedLater") => "İlaçları paylaşınca güveni biraz yumuşadı.",
            "busra" when State.Flags.Contains("busra.hostile") => "İlaç meselesi yüzünden sana güvenmiyor.",
            "busra" => "Markette karşılaştınız; önce yaralısını düşünüyor.",
            _ => "Henüz tam olarak açılmadı."
        };
    }

    private string ComposeCharacterMessage(string characterId, int trust)
    {
        var pressure = State.ThreatLevel >= 65 || State.NoiseLevel >= 60;
        return characterId switch
        {
            "hakan" when pressure => "Bu kadar sesle ilerlersek çıkış değil, hedef oluruz. Sağ tarafı ben kontrol ederim.",
            "hakan" when trust < 40 => "Söz vermek kolay. Bir sonraki kapıda gerçekten yanımda mısın, onu göreceğim.",
            "hakan" => "Köşeleri açık bırakmayın. Sessiz gidiyoruz ama düzensiz değil.",
            "yesking" when pressure => "Ses çok yayıldı. Bir dahaki kapıyı açmadan önce iki saniye dinle, tamam mı?",
            "yesking" when trust < 40 => "Bana doğruyu söylemediğin yerde devre de plan da patlar. Bunu not ettim.",
            "yesking" => "Telsizde parazit var ama boş değil. Bir kelime daha yakalarsam rota netleşir.",
            "bedo" when pressure => "Koşacaksak şimdi koşalım. Ama peşimize mahalleyi takarsak ben sana söylerim, haberin olsun.",
            "bedo" when trust < 40 => "Planın varsa söyle. Yoksa ben çatıyı bulup akarım, kimseye romantik kahramanlık yapmam.",
            "bedo" => "Şu şehir susunca daha beter oluyor. Yine de bir yol var, kokusunu aldım.",
            "busra" when State.Health <= 55 => "Yaranı saklama. Saklanan yara grubu öldürür, kahramanlık değil bu.",
            "busra" when trust < 40 => "İlaç konusunda bencil davranırsan bunu unutamam. Yaralı biri bekliyor.",
            "busra" => "Malzemeyi düzgün paylaşırsak sabaha çıkarız. Panikle harcarsak kimseye yetmez.",
            _ => "Buradayım. Sessiz kalmam gerekiyorsa kalırım, ama gözüm sende."
        };
    }

    private int SceneIndex()
    {
        if (Scene is null)
        {
            return 0;
        }

        const string prefix = "s01c01_scene";
        if (!Scene.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var digits = new string(Scene.Id[prefix.Length..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var index) ? index : 0;
    }

    private (double X, double Y) CurrentMapPoint() => Scene?.Id switch
    {
        "s01c01_scene01_wakeup" => (86, 218),
        "s01c01_scene02_clinic_corridor" or "s01c01_scene03_first_infected" or "s01c01_scene04_hidden_stranger" or "s01c01_scene05_exit_together" => (112, 205),
        "s01c01_scene06_silent_istanbul" => (164, 201),
        "s01c01_scene07_first_shelter" or "s01c01_scene08_power_antenna" or "s01c01_scene09_derya_recording" or "s01c01_scene10_apartment_fall" => (238, 175),
        "s01c01_scene11_bedo_arrives" or "s01c01_scene12_first_night_team" => (323, 148),
        "s01c01_scene13_first_supply_run" or "s01c01_scene14_meet_busra" => (435, 195),
        "s01c01_scene15_chapter_finale" or "s01c01_scene16_ending_narration" => (575, 130),
        _ => (86, 218)
    };

    private IReadOnlyList<MapTimelineItem> CreateMapTimeline()
    {
        var index = SceneIndex();
        return
        [
            new("Sağlık Merkezi", "Başlangıç noktası", index >= 1, index <= 5),
            new("Esenler Sokakları", "Kırmızı işaretler ve sessiz sokaklar", index >= 6, index == 6),
            new("Apartman / Çatı", "Telsiz ve Derya kaydı", index >= 7, index is >= 7 and <= 10),
            new("Terzi Atölyesi", "İlk gece, dağınık ekip", index >= 11, index is 11 or 12),
            new("Küçük Market", "Erzak ve Büşra kararı", index >= 13, index is 13 or 14),
            new("Kuzey Yolu", "Otogar ve sonraki bölüm izi", index >= 15, index >= 15)
        ];
    }

    [RelayCommand]
    private async Task ToggleMessagePanelAsync()
    {
        IsMessagePanelOpen = !IsMessagePanelOpen;
        if (IsMessagePanelOpen)
        {
            foreach (var message in State.Messages)
            {
                message.IsRead = true;
            }
            OnPropertyChanged(nameof(MessageHistory));
            OnPropertyChanged(nameof(UnreadMessageCount));
            await SaveEmergencySnapshotAsync();
        }
    }

    private string ResolveCharacterId(string name) =>
        CharacterDefinitions.Values.FirstOrDefault(character =>
            character.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id ?? "";

    private void BuildHelpOptions(ChoiceViewModel? choice)
    {
        HelpOptions.Clear();
        SelectedHelpOption = null;
        if (choice is null || choice.IsAvailable)
        {
            return;
        }

        var skill = choice.OriginalChoice.Conditions.FirstOrDefault(condition =>
            condition.Type.Equals("SkillCondition", StringComparison.OrdinalIgnoreCase) ||
            condition.Type.Equals("StatCondition", StringComparison.OrdinalIgnoreCase));
        if (skill is null)
        {
            return;
        }

        foreach (var character in State.Characters.Where(character => !character.IsPlayer && character.IsAlive && IsCharacterKnown(character.Id)))
        {
            if (!CharacterDefinitions.TryGetValue(character.Id, out var definition) ||
                !definition.Stats.TryGetValue(skill.Key, out var stat))
            {
                continue;
            }
            var relationship = State.Relationships.FirstOrDefault(item => item.CharacterId == character.Id);
            var trust = relationship?.Trust ?? 50;
            var directorService = new NightDirectorService();
            directorService.LoadState(State.Director);
            var decision = directorService.DecideCharacterHelp(
                State,
                definition,
                character,
                relationship,
                new CharacterHelpRequest
                {
                    CharacterId = character.Id,
                    RequestType = skill.Key,
                    RequiredSkill = skill.Key,
                    Difficulty = skill.Amount,
                    DangerLevel = SelectedChoice?.OriginalChoice.IsDangerous == true ? 70 : 35
                });
            var chance = Math.Clamp(decision.Score, 5, 95);
            if (stat < skill.Amount - 20)
            {
                continue;
            }
            HelpOptions.Add(new HelpOptionViewModel(
                character.Id,
                character.Name,
                skill.Key,
                stat,
                chance,
                $"{definition.Specialty}; {skill.Key} {stat}, güven {trust}, ruh hâli {character.Mood}."));
        }
    }

    [RelayCommand]
    private async Task RequestHelpAsync(HelpOptionViewModel helper)
    {
        if (SelectedChoice is null || SelectedChoice.IsAvailable)
        {
            return;
        }

        SelectedHelpOption = helper;
        var relationship = State.Relationships.FirstOrDefault(item => item.CharacterId == helper.CharacterId);
        var roll = Math.Abs(HashCode.Combine(State.CurrentSceneId, SelectedChoice.Id, helper.CharacterId, State.ChoiceCount)) % 100;
        if (roll < helper.SuccessChance)
        {
            if (relationship is not null)
            {
                relationship.Trust = Math.Clamp(relationship.Trust + (roll > helper.SuccessChance - 15 ? -1 : 3), 0, 100);
            }
            AddMessage(helper.CharacterName, roll > helper.SuccessChance - 15
                ? "İsteksizce yardım edeceğim; bunun karşılığını unutma."
                : "Tamam. Bu işi birlikte hallederiz.", "Yardım");
            _engine.ChooseWithAssistance(State, SelectedChoice.Id, helper.CharacterName);
            await _saveService.SaveAsync(1, State);
            RefreshScene();
            return;
        }

        if (relationship is not null)
        {
            relationship.Trust = Math.Clamp(relationship.Trust - 2, 0, 100);
        }
        AddMessage(helper.CharacterName, "Hayır. Şu an bu riski alamam; başka bir yol bul.", "Reddetme");
        SaveStatus = $"{helper.CharacterName} yardım etmeyi reddetti";
        NotifyStateChanged();
        StartTopMessageQueue();
        await _saveService.SaveAsync(1, State);
    }

    [RelayCommand]
    private void ToggleInventoryUsePanel()
    {
        InventoryFeedback = "";
        IsInventoryUsePanelOpen = !IsInventoryUsePanelOpen;
        OnPropertyChanged(nameof(UsableInventory));
    }

    [RelayCommand]
    private async Task UseInventoryItemAsync(InventoryDisplay display)
    {
        var item = State.Inventory.FirstOrDefault(candidate => candidate.Id == display.Item.Id);
        if (item is null || !IsItemRelevant(item))
        {
            InventoryFeedback = "Bu eşya burada işine yaramaz.";
            return;
        }

        if (Scene?.Id == "radio_room" && item.Id is "fresh_battery" or "battery")
        {
            var batteryChoice = Choices.FirstOrDefault(choice => choice.Id == "use_fresh_battery" && choice.IsAvailable);
            if (batteryChoice is not null)
            {
                IsInventoryUsePanelOpen = false;
                await ApplyChoiceAsync(batteryChoice);
                return;
            }
        }

        if (item.Category == ItemCategory.Health && item.Healing > 0)
        {
            State.Health = Math.Clamp(State.Health + item.Healing, 0, 100);
            InventoryFeedback = $"{item.Name} kullanıldı. Sağlık yükseldi.";
        }
        else if (item.Category == ItemCategory.Food)
        {
            State.Hunger = Math.Clamp(State.Hunger - 25, 0, 100);
            InventoryFeedback = $"{item.Name} tüketildi. Açlık azaldı.";
        }
        else if (item.Category == ItemCategory.Drink)
        {
            State.Thirst = Math.Clamp(State.Thirst - 30, 0, 100);
            InventoryFeedback = $"{item.Name} içildi. Susuzluk azaldı.";
        }
        else if (item.Id is "crowbar" or "wrench" or "screwdriver" or "pliers")
        {
            item.Durability = Math.Max(0, item.Durability - 10);
            State.NoiseLevel = Math.Clamp(State.NoiseLevel + 4, 0, 100);
            State.Flags.Add($"{item.Id}_prepared_{Scene?.Id}");
            InventoryFeedback = $"{item.Name} hazırlandı. Yeni yaklaşım seçilebilir; gürültü biraz arttı.";
            _audio.PlayEffect("audio/sfx/metal_scrape.wav");
        }
        else
        {
            InventoryFeedback = "Bu eşya burada işine yaramaz.";
            return;
        }

        if (item.IsConsumable)
        {
            item.Quantity--;
            if (item.Quantity <= 0)
            {
                State.Inventory.Remove(item);
            }
        }
        NotifyStateChanged();
        QueueTopNotification(new TopMessageViewModel(
            "",
            "Envanter",
            $"{item.Name} kullanıldı.",
            "Envanter",
            "ItemUsed",
            InventoryIcon(item),
            InventoryFeedback,
            new InventoryDisplay(item).Category,
            "Etkisi uygulandı"));
        OnPropertyChanged(nameof(UsableInventory));
        await _saveService.SaveAsync(1, State);
    }

    private bool IsItemRelevant(ItemState item)
    {
        if (!item.IsUsable)
        {
            return false;
        }
        if (item.Category is ItemCategory.Health or ItemCategory.Food or ItemCategory.Drink)
        {
            return true;
        }
        return Scene?.Id switch
        {
            "apartment_01" or "service_door" => item.Id is "crowbar" or "wrench" or "screwdriver" or "pliers",
            "radio_room" => item.Id is "fresh_battery" or "battery" or "wire" or "screwdriver",
            "bedos_fall" or "tunnel_bite" => item.Id is "bandage" or "medkit" or "antiseptic",
            _ => false
        };
    }

    private async void StartClock()
    {
        _clockCancellation?.Cancel();
        _clockCancellation = new CancellationTokenSource();
        try
        {
            while (true)
            {
                await Task.Delay(1000, _clockCancellation.Token);
                State.PlayedSeconds++;
                GameClock.AdvanceRealSecond(State);
                if (State.PlayedSeconds % 30 == 0)
                {
                    State.Fatigue = Math.Clamp(State.Fatigue + 1, 0, 100);
                }
                if (State.PlayedSeconds % 45 == 0)
                {
                    State.Thirst = Math.Clamp(State.Thirst + 2, 0, 100);
                }
                if (State.PlayedSeconds % 60 == 0)
                {
                    State.Hunger = Math.Clamp(State.Hunger + 2, 0, 100);
                }
                if (State.PlayedSeconds % 15 == 0)
                {
                    QueueNeedWarning();
                }
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void QueueNeedWarning()
    {
        var warning = State switch
        {
            { Thirst: >= 80 } => (Key: "thirst_critical", Text: $"{PlayerDefinition.Name} çok susadı.", Tone: "Durum"),
            { Hunger: >= 80 } => (Key: "hunger_critical", Text: $"{PlayerDefinition.Name} açlık hissediyor.", Tone: "Durum"),
            { Fatigue: >= 75 } => (Key: "fatigue_high", Text: $"{PlayerDefinition.Name} yoruluyor.", Tone: "Uyarı"),
            { Sanity: <= 40 } => (Key: "sanity_low", Text: $"{PlayerDefinition.Name} gerginleşiyor.", Tone: "Uyarı"),
            _ => default
        };
        if (string.IsNullOrWhiteSpace(warning.Key))
        {
            return;
        }

        var cooldown = $"need_warning_{warning.Key}_{State.PlayedSeconds / 180}";
        if (!State.Flags.Add(cooldown))
        {
            return;
        }
        AddSystemMessage(warning.Text, warning.Tone, "Durum uyarısı");
        QueueTopNotification(new TopMessageViewModel(
            PlayerDefinition.Id,
            "Durum",
            warning.Text,
            warning.Tone,
            "NeedWarning",
            "!",
            "İhtiyaç seviyesi kritik sınıra yaklaşıyor.",
            "",
            "Üst ihtiyaç uyarısı"));
    }

    private async void StartPeriodicSave()
    {
        _periodicSaveCancellation?.Cancel();
        _periodicSaveCancellation = new CancellationTokenSource();
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), _periodicSaveCancellation.Token);
                if (_settings.AutoSave && State.Ending == EndingKind.None)
                {
                    await _saveService.SaveAsync(1, State, _periodicSaveCancellation.Token);
                    SaveStatus = "Otomatik kaydedildi";
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveToSlotAsync(int slot)
    {
        IsConfirmingOverwrite = false;
        IsSaving = true;
        SaveProgress = 0;
        SaveProgressText = "Kayıt hazırlanıyor...";
        try
        {
            for (var step = 1; step <= 10; step++)
            {
                await Task.Delay(500);
                SaveProgress = step * 10;
                SaveProgressText = step switch
                {
                    <= 3 => "Ekip ve envanter kaydediliyor...",
                    <= 6 => "Hikâye ilerlemesi kaydediliyor...",
                    <= 9 => "Kayıt tamamlanıyor...",
                    _ => "Kayıt tamamlandı."
                };
            }

            await _saveService.SaveAsync(slot, State);
            SaveStatus = $"{slot}. yuvaya kaydedildi";
            await RefreshSaveSlotsAsync();
            await Task.Delay(500);
            IsSavePanelOpen = false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task RefreshSaveSlotsAsync()
    {
        var slots = await _saveService.GetSlotsAsync();
        SaveSlots.Clear();
        foreach (var slot in slots)
        {
            SaveSlots.Add(new SaveSlotViewModel(slot));
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(PlayerDefinition));
        OnPropertyChanged(nameof(Player));
        OnPropertyChanged(nameof(Team));
        OnPropertyChanged(nameof(TeamRelationships));
        OnPropertyChanged(nameof(Inventory));
        OnPropertyChanged(nameof(UsableInventory));
        OnPropertyChanged(nameof(MessageHistory));
        OnPropertyChanged(nameof(UnreadMessageCount));
        OnPropertyChanged(nameof(HistoryEntries));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(EndingTitle));
        OnPropertyChanged(nameof(EndingSummary));
        OnPropertyChanged(nameof(PlayedTimeText));
        OnPropertyChanged(nameof(AliveCount));
        OnPropertyChanged(nameof(CanRestoreCheckpoint));
        OnPropertyChanged(nameof(MapCurrentX));
        OnPropertyChanged(nameof(MapCurrentY));
        OnPropertyChanged(nameof(MapLocationLabel));
        OnPropertyChanged(nameof(MapStatus));
        OnPropertyChanged(nameof(MapTimeline));
        OnPropertyChanged(nameof(ScenarioTasks));
        OnPropertyChanged(nameof(PhysicalAssessment));
        OnPropertyChanged(nameof(MentalAssessment));
        OnPropertyChanged(nameof(ThreatAssessment));
        OnPropertyChanged(nameof(NoiseAssessment));
        UndoCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class ChoiceViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public ChoiceViewModel(ChoiceAvailability availability)
    {
        OriginalChoice = availability.Choice;
        Id = availability.Choice.Id;
        var cleanText = availability.Choice.Text
            .Replace("➦", "", StringComparison.Ordinal)
            .Replace("⚠", "", StringComparison.Ordinal)
            .Replace("⛓", "", StringComparison.Ordinal)
            .Trim();

        Text = availability.IsAvailable ? cleanText : $"{cleanText} — {availability.LockedReason.Replace("⛓", "", StringComparison.Ordinal).Trim()}";
        IsAvailable = availability.IsAvailable;
        IsDangerous = availability.Choice.IsDangerous;
        PreviewImage = availability.Choice.PreviewImage;
        SelectionSummary = string.IsNullOrWhiteSpace(availability.Choice.SelectionSummary)
            ? "Bu karar hikâyeyi yeni bir yöne taşıyacak."
            : availability.Choice.SelectionSummary;
        RiskLevel = availability.Choice.RiskLevel;
        RequirementText = availability.IsAvailable
            ? availability.Choice.Conditions.Count == 0
                ? "Özel gereksinim yok."
                : "Gereksinim karşılandı."
            : availability.LockedReason;
        Symbol = availability.IsAvailable ? availability.Choice.IsDangerous ? "⚠" : "➦" : "⛓";
        ToneBrush = availability.IsAvailable
            ? availability.Choice.IsDangerous ? "#B0814C" : "#B45A5A"
            : "#687066";
    }

    public string Id { get; }
    public StoryChoice OriginalChoice { get; }
    public string Text { get; }
    public bool IsAvailable { get; }
    public bool IsDangerous { get; }
    public string PreviewImage { get; }
    public string SelectionSummary { get; }
    public string RiskLevel { get; }
    public string RequirementText { get; }
    public string Symbol { get; }
    public string ToneBrush { get; }
    public bool CanRequestHelp => !IsAvailable && OriginalChoice.Conditions.Any(condition =>
        condition.Type.Equals("SkillCondition", StringComparison.OrdinalIgnoreCase) ||
        condition.Type.Equals("StatCondition", StringComparison.OrdinalIgnoreCase));
}

public sealed class SaveSlotViewModel(SaveSlotSummary summary)
{
    public int Slot => summary.Slot;
    public bool IsEmpty => summary.IsEmpty;
    public bool IsCorrupted => summary.IsCorrupted;
    public bool CanLoad => !IsEmpty && !IsCorrupted;
    public string CharacterId => summary.CharacterId;
    public string Title => IsEmpty ? $"Kayıt Yuvası {Slot} — Boş" : IsCorrupted ? $"Kayıt Yuvası {Slot} — Bozuk" : $"Kayıt Yuvası {Slot} — {summary.CharacterName}";
    public string Details => IsEmpty
        ? "Yeni bir kayıt için kullanılabilir."
        : IsCorrupted
            ? "Dosya okunamadı. Bu yuvayı silerek yeniden kullanabilirsin."
            : $"{summary.City} • {summary.Chapter} • {summary.PlayedTime:hh\\:mm\\:ss} • {summary.AliveMembers} kişi hayatta";
    public DateTimeOffset? SavedAt => summary.SavedAt;
}

public sealed record TopMessageViewModel(
    string CharacterId,
    string Speaker,
    string Text,
    string Tone,
    string Type = "CharacterMessage",
    string Icon = "!",
    string Detail = "",
    string AmountText = "",
    string Footer = "")
{
    public string ToneBrush => Tone switch
    {
        "Tehlike" => "#B45A5A",
        "Uyarı" => "#B0814C",
        "Envanter" => "#C9C4A3",
        "Keşif" => "#8FA7B3",
        "Görev" => "#B0814C",
        _ => "#7D8D68"
    };

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool HasAmountText => !string.IsNullOrWhiteSpace(AmountText);
    public bool HasFooter => !string.IsNullOrWhiteSpace(Footer);
}

public sealed record InventoryGain(ItemState Item, int Amount);

public sealed record HelpOptionViewModel(
    string CharacterId,
    string CharacterName,
    string Skill,
    int SkillValue,
    int SuccessChance,
    string Reason)
{
    public string HelpLabel => $"{CharacterName}'dan yardım iste";
}

public sealed class MessageDisplay(CharacterMessageState message)
{
    public CharacterMessageState Message => message;
    public string CharacterId => message.CharacterId;
    public string CharacterName => message.CharacterName;
    public string Text => message.Text;
    public string Tone => message.Tone;
    public string RelationshipContext => message.RelationshipContext;
    public string Timestamp => $"Gün {message.Day} • {message.GameTime}";
    public string ReadState => message.IsRead ? "Okundu" : "Yeni";
    public string ToneBrush => message.Tone switch
    {
        "Tehlike" or "Reddetme" => "#B45A5A",
        "Uyarı" => "#B0814C",
        _ => "#7D8D68"
    };
}

public sealed record RelationshipDisplay(
    CharacterState Character,
    CharacterDefinition? Definition,
    int Trust,
    string TrustStatus,
    string LastEvent);

public sealed record MapTimelineItem(
    string Title,
    string Description,
    bool IsUnlocked,
    bool IsCurrent)
{
    public string Marker => IsCurrent ? "●" : IsUnlocked ? "◆" : "○";
    public string ToneBrush => IsCurrent ? "#D3A66B" : IsUnlocked ? "#B45A5A" : "#5A5A61";
    public string StateText => IsCurrent ? "Şu an buradasın" : IsUnlocked ? "Geçildi" : "Kilitli";
}

public sealed record ScenarioTaskGroup(int Start, int End, string Title, string Subtasks);

public sealed record ScenarioTaskDisplay(string Title, string Priority, string Status, int Progress, string Hint)
{
    public string StatusText => $"{Priority} • {Status}";
    public string ToneBrush => Status switch
    {
        "Tamamlandı" => "#7D8D68",
        "Aktif" => "#B58955",
        "Başarısız" => "#B45A5A",
        _ => "#626269"
    };
}

public sealed class InventoryDisplay
{
    public InventoryDisplay(ItemState item)
    {
        Item = item;
    }

    public ItemState Item { get; }
    public string Category => Item.Category switch
    {
        ItemCategory.Health => "Sağlık",
        ItemCategory.Food => "Yiyecek",
        ItemCategory.Drink => "İçecek",
        ItemCategory.Tool => "Alet",
        ItemCategory.Weapon => "Silah",
        ItemCategory.Ammunition => "Mühimmat",
        ItemCategory.Quest => "Görev eşyası",
        _ => "Diğer"
    };
    public string Effect => Item.Healing > 0
        ? $"+{Item.Healing} sağlık"
        : Item.Damage > 0
            ? $"{Item.Damage} hasar"
            : Item.IsQuestItem
                ? "Yeni hikâye yolları açabilir"
                : Item.IsConsumable
                    ? "Tek kullanımlık kaynak"
                    : "Çevresel etkileşimlerde kullanılabilir";
    public string Usage => Item.IsUsable ? "Kullanılabilir" : "Şu anda kullanılamaz";
    public string Durability => Item.Category == ItemCategory.Tool ? $"Dayanıklılık: %{Item.Durability}" : "";
}
