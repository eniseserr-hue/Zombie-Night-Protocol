using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ZombieNightProtocol.App;

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

public sealed class AssetImageConverter : IValueConverter
{
    private static readonly object LogLock = new();
    private static readonly HashSet<string> LoggedPaths = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value?.ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var assetKind = parameter?.ToString();
        var relativePath = assetKind switch
        {
            "character" => Path.Combine("images", "characters", $"{key}.webp"),
            _ => key.Replace('/', Path.DirectorySeparatorChar)
        };

        var contentRoot = Path.Combine(AppContext.BaseDirectory, "content");
        var preferredPath = Path.Combine(contentRoot, relativePath);
        var image = Load(preferredPath) ?? Load(Path.ChangeExtension(preferredPath, ".png"));
        if (image is not null)
        {
            return image;
        }

        LogMissing(preferredPath);
        var fallback = assetKind == "character"
            ? Path.Combine(contentRoot, "images", "characters", "fallback.webp")
            : Path.Combine(contentRoot, "images", "scenes", "fallback.webp");
        return Load(fallback) ?? Load(Path.ChangeExtension(fallback, ".png"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static BitmapImage? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (FileFormatException)
        {
            return null;
        }
    }

    private static void LogMissing(string path)
    {
        lock (LogLock)
        {
            if (!LoggedPaths.Add(path))
            {
                return;
            }

            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ZombieNightProtocol",
                    "Logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(
                    Path.Combine(folder, $"asset-missing-{DateTime.Now:yyyy-MM-dd}.log"),
                    $"{DateTimeOffset.Now:O} Görsel yüklenemedi: {path}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

public sealed class MissingAssetVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var relativePath = value?.ToString();
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Visibility.Visible;
        }

        var path = Path.Combine(
            AppContext.BaseDirectory,
            "content",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
