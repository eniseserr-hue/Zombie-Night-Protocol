using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using ZombieNightProtocol.Infrastructure;

namespace ZombieNightProtocol.Updater;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = UpdaterOptions.Parse(args);
            await WaitForGameAsync(options.ProcessId);
            var manifest = await LoadManifestAsync(options.ManifestPath);
            await ApplyAsync(options, manifest);
            RestartGame(options.ApplicationRoot);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Güncelleme uygulanamadı: {exception}");
            return 1;
        }
    }

    private static async Task<UpdateManifest> LoadManifestAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonDefaults.Options)
            ?? throw new InvalidDataException("Güncelleme manifesti boş.");
    }

    private static async Task WaitForGameAsync(int processId)
    {
        if (processId <= 0) return;
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
        }
    }

    private static async Task ApplyAsync(UpdaterOptions options, UpdateManifest manifest)
    {
        if (!await HashService.VerifyAsync(options.PackagePath, manifest.Sha256))
        {
            throw new InvalidDataException("Paket SHA-256 doğrulaması başarısız.");
        }

        var extractedRoot = Path.Combine(options.StagingRoot, "extracted");
        if (Directory.Exists(extractedRoot))
        {
            Directory.Delete(extractedRoot, true);
        }
        Directory.CreateDirectory(extractedRoot);
        ZipFile.ExtractToDirectory(options.PackagePath, extractedRoot, overwriteFiles: true);

        var backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZombieNightProtocol",
            "Backup",
            DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(backupRoot);
        var changed = new List<(string Target, string? Backup, bool Existed)>();

        try
        {
            foreach (var staged in Directory.EnumerateFiles(extractedRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(extractedRoot, staged);
                var target = SafePath.ValidateRelativeFile(options.ApplicationRoot, relative);
                var backup = SafePath.ValidateRelativeFile(backupRoot, relative);
                var existed = File.Exists(target);
                if (existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, true);
                }
                changed.Add((target, existed ? backup : null, existed));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(staged, target, true);
            }

            await AtomicFile.WriteJsonAsync(Path.Combine(options.ApplicationRoot, "update-manifest.json"), manifest);
            Directory.Delete(options.StagingRoot, true);
        }
        catch
        {
            Rollback(changed);
            throw;
        }
    }

    private static void Rollback(IEnumerable<(string Target, string? Backup, bool Existed)> changed)
    {
        foreach (var item in changed.Reverse())
        {
            if (item.Existed && item.Backup is not null && File.Exists(item.Backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
                File.Copy(item.Backup, item.Target, true);
            }
            else if (!item.Existed && File.Exists(item.Target))
            {
                File.Delete(item.Target);
            }
        }
    }

    private static void RestartGame(string applicationRoot)
    {
        var executable = Path.Combine(applicationRoot, "ZombieNightProtocol.exe");
        if (File.Exists(executable))
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }
    }
}

public sealed record UpdaterOptions(
    int ProcessId,
    string ApplicationRoot,
    string StagingRoot,
    string ManifestPath,
    string PackagePath)
{
    public static UpdaterOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length - 1; index += 2)
        {
            values[args[index]] = args[index + 1];
        }

        if (!values.TryGetValue("--app", out var app) ||
            !values.TryGetValue("--staging", out var staging) ||
            !values.TryGetValue("--manifest", out var manifest) ||
            !values.TryGetValue("--package", out var package))
        {
            throw new ArgumentException("Eksik updater argümanı.");
        }
        _ = values.TryGetValue("--process", out var process);
        return new UpdaterOptions(
            int.TryParse(process, out var processId) ? processId : 0,
            Path.GetFullPath(app),
            Path.GetFullPath(staging),
            Path.GetFullPath(manifest),
            Path.GetFullPath(package));
    }
}
