using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZombieNightProtocol.Core;
using ZombieNightProtocol.Infrastructure;

namespace ZombieNightProtocol.App;

public partial class App : Application
{
    private IHost? _host;
    private bool _isHandlingDispatcherException;
    private string _lastDispatcherExceptionSignature = "";
    private DateTimeOffset _lastDispatcherExceptionAt = DateTimeOffset.MinValue;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = new ApplicationPaths();
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "content");
        var updateConfiguration = LoadUpdateConfiguration();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddProvider(new DailyFileLoggerProvider(paths));
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(paths);
                services.AddSingleton(updateConfiguration);
                services.AddSingleton(new HttpClient { DefaultRequestHeaders = { UserAgent = { new("ZombieNightProtocol", GameConstants.Version) } } });
                services.AddSingleton<IStoryRepository>(provider => new JsonSeasonStoryRepository(contentRoot, provider.GetRequiredService<ILogger<JsonSeasonStoryRepository>>()));
                services.AddSingleton<ICharacterRepository>(provider => new JsonCharacterRepository(contentRoot, provider.GetRequiredService<ILogger<JsonCharacterRepository>>()));
                services.AddSingleton<ISaveService, AtomicSaveService>();
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<IGameDiagnostics, LoggerDiagnostics>();
                services.AddSingleton(provider => new AudioService(contentRoot, provider.GetRequiredService<IGameDiagnostics>()));
                services.AddSingleton<GitHubUpdateService>();
                services.AddSingleton<PatchDownloader>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        await _host.Services.GetRequiredService<MainViewModel>().InitializeAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            if (_host.Services.GetService<MainViewModel>() is { } viewModel)
            {
                await viewModel.SaveActiveSessionAsync();
            }
            _host.Services.GetService<AudioService>()?.StopAll();
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private static UpdateConfiguration LoadUpdateConfiguration()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "update-config.json");
        if (!File.Exists(path))
        {
            return new UpdateConfiguration();
        }
        try
        {
            return JsonSerializer.Deserialize<UpdateConfiguration>(File.ReadAllText(path), JsonDefaults.Options)
                ?? new UpdateConfiguration();
        }
        catch (JsonException)
        {
            return new UpdateConfiguration();
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal(e.Exception);
        var signature = $"{e.Exception.GetType().FullName}:{e.Exception.Message}";
        var isRepeated = signature == _lastDispatcherExceptionSignature &&
                         DateTimeOffset.Now - _lastDispatcherExceptionAt < TimeSpan.FromSeconds(10);
        _lastDispatcherExceptionSignature = signature;
        _lastDispatcherExceptionAt = DateTimeOffset.Now;

        if (_isHandlingDispatcherException || isRepeated)
        {
            e.Handled = true;
            return;
        }

        _isHandlingDispatcherException = true;
        try
        {
            MessageBox.Show(
                "Beklenmeyen bir arayüz hatası oluştu. Ayrıntılar log dosyasına kaydedildi ve ana menüye dönüldü.",
                GameConstants.ApplicationName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            if (_host?.Services.GetService<MainViewModel>() is { } viewModel)
            {
                viewModel.RecoverFromUiError();
            }
            e.Handled = true;
        }
        finally
        {
            _isHandlingDispatcherException = false;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogFatal(exception);
        }
    }

    private static void LogFatal(Exception exception)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZombieNightProtocol", "Logs");
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, $"fatal-{DateTime.Now:yyyy-MM-dd}.log"), $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}");
    }
}
