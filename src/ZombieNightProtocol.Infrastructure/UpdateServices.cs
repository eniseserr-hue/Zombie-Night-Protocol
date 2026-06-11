using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ZombieNightProtocol.Infrastructure;

public sealed class UpdateConfiguration
{
    public string Owner { get; init; } = "eniseserr-hue";
    public string Repository { get; init; } = "Zombie-Night-Protocol";
    public string ManifestUrl { get; init; } = "https://raw.githubusercontent.com/eniseserr-hue/Zombie-Night-Protocol/main/update-manifest.json";
    public string Channel { get; init; } = "stable";
    public int TimeoutSeconds { get; init; } = 8;
}

public sealed class UpdateManifest
{
    public string Version { get; init; } = "1.0.0";
    public bool Mandatory { get; init; }
    public List<string> ReleaseNotes { get; init; } = [];
    public string DownloadUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long PackageSize { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    [JsonIgnore]
    public bool IsRequired { get; init; }
    [JsonIgnore]
    public long DownloadSize { get; init; }
    public List<UpdateFileEntry> Files { get; init; } = [];
    [JsonIgnore]
    public bool RequiresUpdate => Mandatory || IsRequired;
    [JsonIgnore]
    public long EffectivePackageSize => PackageSize > 0 ? PackageSize : DownloadSize;
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

public sealed record UpdateCheckResult(bool IsAvailable, bool IsRequired, UpdateManifest? Manifest, string Message, bool CheckFailed = false);

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
            return new UpdateCheckResult(available, available && manifest.RequiresUpdate, manifest, available ? "Yeni sürüm bulundu." : "Oyun güncel.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            logger.LogWarning(exception, "Güncelleme kontrolü yapılamadı.");
            return new UpdateCheckResult(false, false, null, "Güncelleme sunucusuna ulaşılamadı.", true);
        }
    }
}

public sealed class PatchDownloader(HttpClient client, ILogger<PatchDownloader> logger)
{
    public async Task<string> DownloadPackageAsync(
        UpdateManifest manifest,
        string stagingRoot,
        IProgress<(long Downloaded, long Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifest.DownloadUrl) || string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            throw new InvalidDataException("Paket URL veya SHA-256 değeri eksik.");
        }

        Directory.CreateDirectory(stagingRoot);
        var destination = Path.Combine(stagingRoot, $"ZombieNightProtocol-{manifest.Version}.zip");
        using var response = await client.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? manifest.EffectivePackageSize;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            progress?.Report((downloaded, Math.Max(1, total)));
        }
        await target.FlushAsync(cancellationToken);
        if (!await HashService.VerifyAsync(destination, manifest.Sha256, cancellationToken))
        {
            File.Delete(destination);
            throw new InvalidDataException("İndirilen güncelleme paketinin SHA-256 doğrulaması başarısız.");
        }
        logger.LogInformation("Güncelleme paketi indirildi ve doğrulandı: {Path}", destination);
        return destination;
    }

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

    public static void StartUpdater(string updaterPath, string applicationRoot, string stagingRoot, string manifestPath, string packagePath, int processId)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = updaterPath,
            UseShellExecute = true,
            Arguments = $"--process {processId} --app \"{applicationRoot}\" --staging \"{stagingRoot}\" --manifest \"{manifestPath}\" --package \"{packagePath}\""
        });
    }
}
