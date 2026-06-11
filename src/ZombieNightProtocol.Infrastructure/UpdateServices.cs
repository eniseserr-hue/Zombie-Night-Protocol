using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ZombieNightProtocol.Infrastructure;

public sealed class UpdateConfiguration
{
    public string Owner { get; init; } = "OWNER_NAME";
    public string Repository { get; init; } = "REPOSITORY_NAME";
    public string ManifestUrl { get; init; } = "https://raw.githubusercontent.com/OWNER_NAME/REPOSITORY_NAME/main/content/updates/manifest.json";
    public string Channel { get; init; } = "stable";
    public int TimeoutSeconds { get; init; } = 8;
}

public sealed class UpdateManifest
{
    public string Version { get; init; } = "1.0.0";
    public bool IsRequired { get; init; }
    public long DownloadSize { get; init; }
    public string ReleaseNotes { get; init; } = "";
    public List<UpdateFileEntry> Files { get; init; } = [];
}

public sealed class UpdateFileEntry
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    public string DownloadUrl { get; init; } = "";
    public bool IsRequired { get; init; } = true;
    public bool Delete { get; init; }
}

public sealed record UpdatePlan(IReadOnlyList<UpdateFileEntry> Download, IReadOnlyList<UpdateFileEntry> Delete)
{
    public bool HasChanges => Download.Count > 0 || Delete.Count > 0;
}

public sealed record UpdateCheckResult(bool IsAvailable, bool IsRequired, UpdateManifest? Manifest, string Message);

public static class HashService
{
    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<bool> VerifyAsync(string path, string expected, CancellationToken cancellationToken = default) =>
        File.Exists(path) && string.Equals(await Sha256Async(path, cancellationToken), expected, StringComparison.OrdinalIgnoreCase);
}

public sealed class UpdatePlanner
{
    public async Task<UpdatePlan> CreateAsync(
        string applicationRoot,
        UpdateManifest remote,
        CancellationToken cancellationToken = default)
    {
        var download = new List<UpdateFileEntry>();
        var delete = new List<UpdateFileEntry>();
        foreach (var entry in remote.Files)
        {
            var localPath = SafePath.ValidateRelativeFile(applicationRoot, entry.Path);
            if (entry.Delete)
            {
                if (File.Exists(localPath))
                {
                    delete.Add(entry);
                }
                continue;
            }

            if (!await HashService.VerifyAsync(localPath, entry.Sha256, cancellationToken))
            {
                download.Add(entry);
            }
        }
        return new UpdatePlan(download, delete);
    }
}

public sealed class GitHubUpdateService(
    HttpClient client,
    UpdateConfiguration configuration,
    ILogger<GitHubUpdateService> logger)
{
    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        if (configuration.Owner == "OWNER_NAME" || configuration.Repository == "REPOSITORY_NAME")
        {
            return new UpdateCheckResult(false, false, null, "Güncelleme deposu henüz yapılandırılmadı.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(configuration.TimeoutSeconds));
            using var response = await client.GetAsync(configuration.ManifestUrl, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonDefaults.Options, timeout.Token)
                ?? throw new InvalidDataException("Uzak manifest boş.");
            var available = Version.TryParse(manifest.Version, out var remoteVersion) && remoteVersion > currentVersion;
            return new UpdateCheckResult(available, available && manifest.IsRequired, manifest, available ? "Yeni sürüm bulundu." : "Oyun güncel.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            logger.LogWarning(exception, "Güncelleme kontrolü yapılamadı; çevrimdışı devam ediliyor.");
            return new UpdateCheckResult(false, false, null, "Güncelleme sunucusuna ulaşılamadı. Çevrimdışı devam ediliyor.");
        }
    }
}

public sealed class PatchDownloader(HttpClient client, ILogger<PatchDownloader> logger)
{
    public async Task DownloadAsync(
        UpdatePlan plan,
        string stagingRoot,
        IProgress<(string File, long Downloaded, long Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(stagingRoot);
        long totalDownloaded = 0;
        var total = plan.Download.Sum(entry => entry.Size);
        foreach (var entry in plan.Download)
        {
            var destination = SafePath.ValidateRelativeFile(stagingRoot, entry.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var response = await client.GetAsync(entry.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalDownloaded += read;
                progress?.Report((entry.Path, totalDownloaded, total));
            }
            await target.FlushAsync(cancellationToken);
            if (!await HashService.VerifyAsync(destination, entry.Sha256, cancellationToken))
            {
                throw new InvalidDataException($"İndirilen dosyanın hash değeri geçersiz: {entry.Path}");
            }
            logger.LogInformation("Güncelleme dosyası indirildi: {Path}", entry.Path);
        }
    }

    public static void StartUpdater(string updaterPath, string applicationRoot, string stagingRoot, string manifestPath, int processId)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = updaterPath,
            UseShellExecute = true,
            Arguments = $"--process {processId} --app \"{applicationRoot}\" --staging \"{stagingRoot}\" --manifest \"{manifestPath}\""
        });
    }
}
