using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZombieNightProtocol.Core;

namespace ZombieNightProtocol.Infrastructure;

public sealed class ApplicationPaths
{
    public ApplicationPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZombieNightProtocol");
        Saves = Path.Combine(Root, "Saves");
        Logs = Path.Combine(Root, "Logs");
        Updates = Path.Combine(Root, "Updates");
        Staging = Path.Combine(Updates, "Staging");
        Settings = Path.Combine(Root, "Settings");
        Directory.CreateDirectory(Saves);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Staging);
        Directory.CreateDirectory(Settings);
    }

    public string Root { get; }
    public string Saves { get; }
    public string Logs { get; }
    public string Updates { get; }
    public string Staging { get; }
    public string Settings { get; }
}

public static class AtomicFile
{
    public static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Dosya klasörü bulunamadı."));
        var temporary = path + ".tmp";
        var backup = path + ".bak";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonDefaults.Options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        if (File.Exists(path))
        {
            File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporary, path);
        }
    }
}

public sealed class AtomicSaveService(
    ApplicationPaths paths,
    ILogger<AtomicSaveService> logger) : ISaveService
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public async Task<IReadOnlyList<SaveSlotSummary>> GetSlotsAsync(CancellationToken cancellationToken = default)
    {
        var slots = new List<SaveSlotSummary>();
        for (var slot = 1; slot <= GameConstants.SaveSlotCount; slot++)
        {
            var state = await LoadInternalAsync(slot, logCorruption: false, cancellationToken);
            var path = SlotPath(slot);
            if (state is null)
            {
                var hasSaveFile = File.Exists(path) || File.Exists(path + ".bak");
                slots.Add(new SaveSlotSummary(slot, !hasSaveFile, hasSaveFile, "", "", "", "", TimeSpan.Zero, null, 0));
                continue;
            }

            slots.Add(new SaveSlotSummary(
                slot,
                false,
                false,
                state.SelectedCharacterId,
                state.Characters.FirstOrDefault(character => character.IsPlayer)?.Name ?? state.SelectedCharacterId,
                state.CurrentChapterId,
                state.City,
                TimeSpan.FromSeconds(state.PlayedSeconds),
                state.LastSavedAt,
                state.Characters.Count(character => character.IsAlive)));
        }
        return slots;
    }

    public Task<GameState?> LoadAsync(int slot, CancellationToken cancellationToken = default) =>
        LoadInternalAsync(slot, logCorruption: true, cancellationToken);

    public async Task SaveAsync(int slot, GameState state, CancellationToken cancellationToken = default)
    {
        ValidateSlot(slot);
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            state.LastSavedAt = DateTimeOffset.Now;
            await AtomicFile.WriteJsonAsync(SlotPath(slot), state, cancellationToken);
            logger.LogInformation("Kayıt yuvası {Slot} yazıldı.", slot);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public Task DeleteAsync(int slot, CancellationToken cancellationToken = default)
    {
        ValidateSlot(slot);
        cancellationToken.ThrowIfCancellationRequested();
        var path = SlotPath(slot);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        if (File.Exists(path + ".bak"))
        {
            File.Delete(path + ".bak");
        }
        return Task.CompletedTask;
    }

    private async Task<GameState?> LoadInternalAsync(int slot, bool logCorruption, CancellationToken cancellationToken)
    {
        ValidateSlot(slot);
        var path = SlotPath(slot);
        var backup = path + ".bak";
        if (!File.Exists(path) && !File.Exists(backup))
        {
            return null;
        }

        try
        {
            return await ReadStateAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            if (logCorruption)
            {
                logger.LogWarning(exception, "{Slot}. kayıt yuvası bozuk; yedek deneniyor.", slot);
            }
            if (!File.Exists(backup))
            {
                return null;
            }
            try
            {
                var recovered = await ReadStateAsync(backup, cancellationToken);
                File.Copy(backup, path, overwrite: true);
                logger.LogInformation("{Slot}. kayıt yuvası yedekten geri yüklendi.", slot);
                return recovered;
            }
            catch (Exception backupException) when (backupException is JsonException or IOException or InvalidDataException)
            {
                if (logCorruption)
                {
                    logger.LogError(backupException, "{Slot}. kayıt yuvasının yedeği de bozuk.", slot);
                }
                return null;
            }
        }
    }

    private static async Task<GameState> ReadStateAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var state = await JsonSerializer.DeserializeAsync<GameState>(stream, JsonDefaults.Options, cancellationToken);
        if (state is null || state.SaveVersion < 1 || string.IsNullOrWhiteSpace(state.CurrentSceneId))
        {
            throw new InvalidDataException("Kayıt biçimi geçersiz.");
        }
        return state;
    }

    private string SlotPath(int slot) => Path.Combine(paths.Saves, $"slot-{slot}.json");

    private static void ValidateSlot(int slot)
    {
        if (slot is < 1 or > GameConstants.SaveSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }
}

public sealed class JsonSettingsService(
    ApplicationPaths paths,
    ILogger<JsonSettingsService> logger) : ISettingsService
{
    private string FilePath => Path.Combine(paths.Settings, "settings.json");
    public string SettingsFolder => paths.Settings;
    public string LogsFolder => paths.Logs;
    public string SavesFolder => paths.Saves;

    public async Task<GameSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new GameSettings();
        }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<GameSettings>(stream, JsonDefaults.Options, cancellationToken)
                ?? new GameSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(exception, "Ayar dosyası bozuk; varsayılan ayarlara dönüldü.");
            return new GameSettings();
        }
    }

    public Task SaveAsync(GameSettings settings, CancellationToken cancellationToken = default) =>
        AtomicFile.WriteJsonAsync(FilePath, settings, cancellationToken);
}
