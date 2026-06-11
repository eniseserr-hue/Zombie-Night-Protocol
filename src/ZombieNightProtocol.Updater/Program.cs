using System.Diagnostics;
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
            Console.Error.WriteLine($"Güncelleme uygulanamadı: {exception.Message}");
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
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
            // Oyun zaten kapanmış.
        }
    }

    private static async Task ApplyAsync(UpdaterOptions options, UpdateManifest manifest)
    {
        var backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZombieNightProtocol",
            "Updates",
            "Backup",
            DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(backupRoot);
        var changed = new List<(string Target, string? Backup, bool Existed)>();

        try
        {
            foreach (var entry in manifest.Files)
            {
                var target = SafePath.ValidateRelativeFile(options.ApplicationRoot, entry.Path);
                var backup = SafePath.ValidateRelativeFile(backupRoot, entry.Path);
                var existed = File.Exists(target);
                if (existed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, overwrite: true);
                }

                changed.Add((target, existed ? backup : null, existed));
                if (entry.Delete)
                {
                    if (existed)
                    {
                        File.Delete(target);
                    }
                    continue;
                }

                var staged = SafePath.ValidateRelativeFile(options.StagingRoot, entry.Path);
                if (!await HashService.VerifyAsync(staged, entry.Sha256))
                {
                    throw new InvalidDataException($"Staging hash doğrulaması başarısız: {entry.Path}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(staged, target, overwrite: true);
                if (!await HashService.VerifyAsync(target, entry.Sha256))
                {
                    throw new InvalidDataException($"Uygulanan dosya hash doğrulaması başarısız: {entry.Path}");
                }
            }

            var localManifest = Path.Combine(options.ApplicationRoot, "manifest.json");
            await AtomicFile.WriteJsonAsync(localManifest, manifest);
            Directory.Delete(options.StagingRoot, recursive: true);
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
                File.Copy(item.Backup, item.Target, overwrite: true);
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
        if (!File.Exists(executable))
        {
            executable = Path.Combine(applicationRoot, "ZombieNightProtocol.App.exe");
        }
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
    string ManifestPath)
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
            !values.TryGetValue("--manifest", out var manifest))
        {
            throw new ArgumentException("Kullanım: --process <pid> --app <klasör> --staging <klasör> --manifest <dosya>");
        }

        _ = values.TryGetValue("--process", out var process);
        return new UpdaterOptions(
            int.TryParse(process, out var processId) ? processId : 0,
            Path.GetFullPath(app),
            Path.GetFullPath(staging),
            Path.GetFullPath(manifest));
    }
}
