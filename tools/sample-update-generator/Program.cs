using System.Security.Cryptography;
using System.Text.Json;

if (args.Length < 3)
{
    Console.Error.WriteLine("Kullanım: sample-update-generator <release-klasörü> <sürüm> <base-url>");
    return 1;
}

var root = Path.GetFullPath(args[0]);
var version = args[1];
var baseUrl = args[2].TrimEnd('/');
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"Klasör bulunamadı: {root}");
    return 2;
}

var files = new List<object>();
foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
             .Where(path => !path.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
{
    await using var stream = File.OpenRead(path);
    var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
    files.Add(new
    {
        path = relative,
        size = new FileInfo(path).Length,
        sha256 = hash,
        version,
        downloadUrl = $"{baseUrl}/{relative}",
        isRequired = true,
        delete = false
    });
}

var manifest = new
{
    version,
    isRequired = true,
    downloadSize = files.Sum(file => (long)file.GetType().GetProperty("size")!.GetValue(file)!),
    releaseNotes = $"Zombie Night Protocol {version} güncellemesi.",
    files
};
var output = Path.Combine(root, "manifest.json");
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);
return 0;
