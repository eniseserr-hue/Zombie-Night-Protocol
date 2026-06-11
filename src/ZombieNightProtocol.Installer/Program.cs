using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ZombieNightProtocol.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}

internal sealed class InstallerForm : Form
{
    private const string ManifestUrl = "https://raw.githubusercontent.com/eniseserr-hue/Zombie-Night-Protocol/main/update-manifest.json";
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _installButton = new();
    private readonly CheckBox _launchAfterInstall = new();
    private readonly string _installFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZombieNightProtocol",
        "App");

    public InstallerForm()
    {
        Text = "Zombie Night Protocol Kurulum";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(620, 360);
        BackColor = Color.FromArgb(12, 12, 14);
        ForeColor = Color.FromArgb(232, 228, 220);
        Font = new Font("Segoe UI", 10, FontStyle.Regular);
        Icon = ExtractAppIcon();

        var title = new Label
        {
            Text = "Zombie Night Protocol",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 24, FontStyle.Bold),
            Location = new Point(34, 28)
        };

        var subtitle = new Label
        {
            Text = "Oyunu indirir, kurar ve masaustune kisayol ekler.",
            AutoSize = true,
            ForeColor = Color.FromArgb(184, 91, 96),
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Location = new Point(38, 78)
        };

        var folder = new Label
        {
            Text = $"Kurulum yeri: {_installFolder}",
            AutoEllipsis = true,
            Width = 545,
            Height = 24,
            ForeColor = Color.FromArgb(155, 153, 149),
            Location = new Point(38, 122)
        };

        _status.Text = "Hazir. Kurulumu baslatmak icin butona bas.";
        _status.AutoEllipsis = true;
        _status.Width = 545;
        _status.Height = 50;
        _status.Location = new Point(38, 164);

        _progress.Width = 545;
        _progress.Height = 18;
        _progress.Location = new Point(38, 224);
        _progress.Style = ProgressBarStyle.Continuous;

        _launchAfterInstall.Text = "Kurulum bitince oyunu ac";
        _launchAfterInstall.Checked = true;
        _launchAfterInstall.AutoSize = true;
        _launchAfterInstall.Location = new Point(38, 266);

        _installButton.Text = "Kur";
        _installButton.Width = 160;
        _installButton.Height = 42;
        _installButton.Location = new Point(423, 286);
        _installButton.BackColor = Color.FromArgb(116, 50, 56);
        _installButton.ForeColor = Color.White;
        _installButton.FlatStyle = FlatStyle.Flat;
        _installButton.FlatAppearance.BorderColor = Color.FromArgb(184, 91, 96);
        _installButton.Click += async (_, _) => await InstallAsync();

        Controls.AddRange([title, subtitle, folder, _status, _progress, _launchAfterInstall, _installButton]);
    }

    private async Task InstallAsync()
    {
        _installButton.Enabled = false;
        try
        {
            SetStatus("Guncelleme bilgisi aliniyor...", 2);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ZombieNightProtocol.Setup/1.0.4");
            var manifest = await http.GetFromJsonAsync<UpdateManifest>(ManifestUrl)
                ?? throw new InvalidOperationException("Guncelleme manifesti okunamadi.");

            var tempRoot = Path.Combine(Path.GetTempPath(), "ZombieNightProtocol.Setup");
            Directory.CreateDirectory(tempRoot);
            var packagePath = Path.Combine(tempRoot, $"ZombieNightProtocol-{manifest.Version}.zip");
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }

            SetStatus($"Oyun indiriliyor ({FormatBytes(manifest.EffectivePackageSize)})...", 5);
            await DownloadAsync(http, manifest.DownloadUrl, packagePath, manifest.EffectivePackageSize);

            SetStatus("Paket dogrulaniyor...", 82);
            var hash = await Sha256Async(packagePath);
            if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Indirilen paket bozuk gorunuyor. Hash dogrulamasi basarisiz.");
            }

            SetStatus("Oyun kuruluyor...", 88);
            var parent = Path.GetDirectoryName(_installFolder)!;
            Directory.CreateDirectory(parent);
            var backup = Path.Combine(parent, "App.backup");
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, true);
            }

            if (Directory.Exists(_installFolder))
            {
                Directory.Move(_installFolder, backup);
            }

            try
            {
                ZipFile.ExtractToDirectory(packagePath, _installFolder);
            }
            catch
            {
                if (Directory.Exists(_installFolder))
                {
                    Directory.Delete(_installFolder, true);
                }
                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, _installFolder);
                }
                throw;
            }

            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, true);
            }

            SetStatus("Masaustu kisayolu olusturuluyor...", 96);
            var exePath = Path.Combine(_installFolder, "ZombieNightProtocol.exe");
            CreateShortcut(exePath);

            SetStatus("Kurulum tamamlandi.", 100);
            if (_launchAfterInstall.Checked)
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = _installFolder });
            }

            _installButton.Text = "Tamam";
            _installButton.Enabled = true;
            _installButton.Click -= async (_, _) => await InstallAsync();
            _installButton.Click += (_, _) => Close();
        }
        catch (Exception exception)
        {
            SetStatus("Kurulum basarisiz: " + exception.Message, 0);
            _installButton.Enabled = true;
            _installButton.Text = "Tekrar Dene";
        }
    }

    private async Task DownloadAsync(HttpClient http, string url, string destination, long expectedSize)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength.GetValueOrDefault(expectedSize);
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(destination);

        var buffer = new byte[1024 * 128];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            var percent = total <= 0 ? 10 : 5 + (int)Math.Clamp(readTotal * 75 / total, 0, 75);
            SetStatus($"Oyun indiriliyor... {FormatBytes(readTotal)} / {FormatBytes(total)}", percent);
        }
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void CreateShortcut(string exePath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "Zombie Night Protocol.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows kisayol sistemi bulunamadi.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Windows kisayol sistemi baslatilamadi.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = exePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
        shortcut.IconLocation = exePath;
        shortcut.Description = "Zombie Night Protocol";
        shortcut.Save();
    }

    private void SetStatus(string text, int progress)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text, progress));
            return;
        }

        _status.Text = text;
        _progress.Value = Math.Clamp(progress, 0, 100);
    }

    private static string FormatBytes(long value)
    {
        if (value <= 0)
        {
            return "--";
        }

        var mb = value / 1024d / 1024d;
        return $"{mb:N1} MB";
    }

    private static Icon? ExtractAppIcon()
    {
        try
        {
            return Environment.ProcessPath is { } path
                ? Icon.ExtractAssociatedIcon(path)
                : null;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class UpdateManifest
{
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; init; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
    [JsonPropertyName("packageSize")] public long PackageSize { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    public long EffectivePackageSize => PackageSize > 0 ? PackageSize : Size;
}
