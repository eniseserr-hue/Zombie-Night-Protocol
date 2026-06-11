using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ZombieNightProtocol.Core;

namespace ZombieNightProtocol.Infrastructure;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed class JsonStoryRepository(
    string contentRoot,
    ILogger<JsonStoryRepository> logger) : IStoryRepository
{
    private readonly string _storyPath = SafePath.CombineWithin(contentRoot, "stories", "prototype-tr", "story.json");

    public async Task<StoryPackage> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(_storyPath);
        var story = await JsonSerializer.DeserializeAsync<StoryPackage>(stream, JsonDefaults.Options, cancellationToken)
            ?? throw new InvalidDataException("Hikâye dosyası boş.");
        var issues = new StoryValidator().Validate(story);
        foreach (var issue in issues)
        {
            if (issue.Severity == "Error")
            {
                logger.LogError("Hikâye doğrulama: {Code} {Message}", issue.Code, issue.Message);
            }
            else
            {
                logger.LogWarning("Hikâye doğrulama: {Code} {Message}", issue.Code, issue.Message);
            }
        }
        if (issues.Any(issue => issue.Severity == "Error"))
        {
            throw new InvalidDataException("Hikâye doğrulaması başarısız. Ayrıntılar log dosyasına yazıldı.");
        }
        return story;
    }
}

public sealed class JsonSeasonStoryRepository(
    string contentRoot,
    ILogger<JsonSeasonStoryRepository> logger) : IStoryRepository
{
    private readonly string _storiesRoot = SafePath.CombineWithin(contentRoot, "stories");

    public async Task<SeasonDefinition> LoadSeasonAsync(
        string language = "tr",
        string seasonId = "season-01",
        CancellationToken cancellationToken = default)
    {
        var path = SafePath.CombineWithin(_storiesRoot, language, seasonId, "season.json");
        return await ReadJsonAsync<SeasonDefinition>(path, "season.json", cancellationToken);
    }

    public async Task<ChapterDefinition> LoadChapterAsync(
        SeasonDefinition season,
        string chapterId,
        CancellationToken cancellationToken = default)
    {
        var reference = season.Chapters.FirstOrDefault(chapter => chapter.Id.Equals(chapterId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Chapter reference not found: {chapterId}");
        var chapterPath = string.IsNullOrWhiteSpace(reference.ChapterPath)
            ? Path.Combine(chapterId, "chapter.json")
            : reference.ChapterPath;
        var path = SafePath.ValidateRelativeFile(Path.Combine(_storiesRoot, season.Language, season.Id), chapterPath);
        return await ReadJsonAsync<ChapterDefinition>(path, "chapter.json", cancellationToken);
    }

    public async Task<SeasonalScenesDocument> LoadScenesAsync(
        SeasonDefinition season,
        ChapterDefinition chapter,
        CancellationToken cancellationToken = default)
    {
        var sceneFile = string.IsNullOrWhiteSpace(chapter.SceneFile) ? "scenes.json" : chapter.SceneFile;
        var path = SafePath.CombineWithin(_storiesRoot, season.Language, season.Id, chapter.Id, sceneFile);
        return await ReadJsonAsync<SeasonalScenesDocument>(path, "scenes.json", cancellationToken);
    }

    public async Task<StoryPackage> LoadAsStoryPackageAsync(
        string language = "tr",
        string seasonId = "season-01",
        CancellationToken cancellationToken = default)
    {
        var season = await LoadSeasonAsync(language, seasonId, cancellationToken);
        var chapter = await LoadChapterAsync(season, season.StartChapterId, cancellationToken);
        var scenesDocument = await LoadScenesAsync(season, chapter, cancellationToken);
        var package = SeasonStoryAdapter.ToStoryPackage(season, chapter, scenesDocument);
        SeasonAssetValidator.Validate(package, Directory.GetParent(_storiesRoot)?.FullName ?? _storiesRoot);
        var issues = new StoryValidator().Validate(package);
        foreach (var issue in issues)
        {
            if (issue.Severity == "Error")
            {
                logger.LogError("Season story validation: {Code} {Message}", issue.Code, issue.Message);
            }
            else
            {
                logger.LogWarning("Season story validation: {Code} {Message}", issue.Code, issue.Message);
            }
        }
        if (issues.Any(issue => issue.Severity == "Error"))
        {
            throw new InvalidDataException("Season story validation failed. Details were written to logs.");
        }
        return package;
    }

    public Task<StoryPackage> LoadAsync(CancellationToken cancellationToken = default) =>
        LoadAsStoryPackageAsync(cancellationToken: cancellationToken);

    private static async Task<T> ReadJsonAsync<T>(string path, string label, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options, cancellationToken)
                ?? throw new InvalidDataException($"{label} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{label} contains invalid JSON: {exception.Message}", exception);
        }
    }
}

public static class SeasonAssetValidator
{
    public static void Validate(StoryPackage package, string contentRoot)
    {
        var lines = new List<string>
        {
            $"Asset validation: {DateTimeOffset.Now:O}",
            $"Story: {package.Id}"
        };
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scene in package.Scenes)
        {
            ValidatePath(scene.SceneImage, "scene image", scene.Id, null, contentRoot, referenced, lines);
            foreach (var choice in scene.Choices)
            {
                if (string.IsNullOrWhiteSpace(choice.PreviewImage))
                {
                    lines.Add($"Unassigned choice preview: sceneId={scene.Id} choiceId={choice.Id}");
                    continue;
                }
                ValidatePath(choice.PreviewImage, "choice preview", scene.Id, choice.Id, contentRoot, referenced, lines);
            }
        }

        var seasonImageRoot = Path.Combine(contentRoot, "images", "story", "season01");
        if (Directory.Exists(seasonImageRoot))
        {
            foreach (var file in Directory.EnumerateFiles(seasonImageRoot, "*.webp", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(contentRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!referenced.Contains(relative))
                {
                    lines.Add($"Unused asset: path={relative}");
                }
            }
        }

        var logFolder = Path.Combine(Directory.GetParent(contentRoot)?.FullName ?? AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logFolder);
        File.WriteAllLines(Path.Combine(logFolder, "asset-validation.log"), lines);
    }

    private static void ValidatePath(
        string path,
        string kind,
        string sceneId,
        string? choiceId,
        string contentRoot,
        HashSet<string> referenced,
        List<string> lines)
    {
        var context = choiceId is null
            ? $"sceneId={sceneId}"
            : $"sceneId={sceneId} choiceId={choiceId}";
        if (string.IsNullOrWhiteSpace(path))
        {
            lines.Add($"Missing {kind}: {context} path=<empty>");
            return;
        }

        referenced.Add(path);
        if (!path.EndsWith(".webp", StringComparison.Ordinal))
        {
            lines.Add($"Invalid extension: {context} path={path}");
        }
        if (path.Any(character => char.IsWhiteSpace(character) || character > 127))
        {
            lines.Add($"Unsafe asset name: {context} path={path}");
        }

        var fullPath = Path.Combine(contentRoot, path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            lines.Add($"Missing {kind}: {context} path={path}");
            return;
        }

        var fileName = Path.GetFileName(fullPath);
        var actualName = Directory.EnumerateFiles(Path.GetDirectoryName(fullPath)!)
            .Select(Path.GetFileName)
            .FirstOrDefault(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
        if (actualName is not null && !string.Equals(actualName, fileName, StringComparison.Ordinal))
        {
            lines.Add($"Case mismatch: {context} path={path} actual={actualName}");
        }
    }
}

public static class SeasonStoryAdapter
{
    public static StoryPackage ToStoryPackage(
        SeasonDefinition season,
        ChapterDefinition chapter,
        SeasonalScenesDocument scenesDocument)
    {
        var startSceneId = string.IsNullOrWhiteSpace(chapter.StartSceneId)
            ? scenesDocument.Scenes.FirstOrDefault()?.Id ?? ""
            : chapter.StartSceneId;
        return new StoryPackage
        {
            Id = season.Id,
            Version = "1.0.0",
            StartSceneId = startSceneId,
            Scenes = [.. scenesDocument.Scenes.Select(scene => ToStoryScene(season, chapter, scene))]
        };
    }

    private static StoryScene ToStoryScene(SeasonDefinition season, ChapterDefinition chapter, SeasonalScene scene)
    {
        var choices = scene.Choices.Select(ToStoryChoice).ToList();
        return new StoryScene
        {
            Id = scene.Id,
            ChapterId = string.IsNullOrWhiteSpace(scene.ChapterId) ? chapter.Id : scene.ChapterId,
            City = "",
            Location = scene.Location,
            Time = string.IsNullOrWhiteSpace(scene.TimeOfDay) ? "22:45" : scene.TimeOfDay,
            DirectorRules = scene.DirectorRules,
            Tags = [.. scene.Tags],
            TimeOfDay = scene.TimeOfDay,
            ThreatHint = scene.ThreatHint,
            NoiseBase = scene.NoiseBase,
            InfectedDensityHint = scene.InfectedDensityHint,
            LightingHint = scene.LightingHint,
            LightLevel = scene.LightLevel,
            Title = scene.Title,
            SceneImage = string.IsNullOrWhiteSpace(scene.SceneImage) ? "images/scenes/fallback.webp" : scene.SceneImage,
            VoiceMessage = scene.VoiceMessage,
            Content = [new StoryBlock { Type = "narration", Text = scene.Text }],
            Choices = choices,
            IsCheckpoint = !string.IsNullOrWhiteSpace(scene.CheckpointId),
            TimedChoiceSeconds = choices.Where(choice => choice.IsCriticalTimed).Select(_ => scene.Choices.FirstOrDefault(item => item.IsTimed)?.TimeLimitSeconds).FirstOrDefault(),
            DefaultChoiceId = choices.FirstOrDefault(choice => choice.IsCriticalTimed)?.Id
        };
    }

    private static StoryChoice ToStoryChoice(SeasonalChoice choice)
    {
        var effects = new List<StoryEffect>();
        effects.AddRange(choice.Effects);
        if (choice.NoiseChange != 0)
        {
            effects.Add(new StoryEffect { Type = "ChangeNoise", Amount = choice.NoiseChange });
        }
        if (choice.ThreatChange != 0)
        {
            effects.Add(new StoryEffect { Type = "ChangeThreat", Amount = choice.ThreatChange });
        }
        foreach (var trust in choice.CharacterTrustEffects)
        {
            effects.Add(new StoryEffect { Type = "ChangeRelationship", Target = trust.Key, Amount = trust.Value });
        }
        foreach (var inventory in choice.InventoryEffects)
        {
            if (inventory.Type.Equals("quantity", StringComparison.OrdinalIgnoreCase))
            {
                effects.Add(new StoryEffect { Type = "ChangeItemQuantity", Key = inventory.ItemId, Amount = inventory.Amount });
            }
        }

        return new StoryChoice
        {
            Id = choice.Id,
            Text = choice.Text,
            NextSceneId = choice.TargetSceneId,
            PreviewImage = choice.ChoicePreviewImage,
            SelectionSummary = choice.Summary,
            Conditions = [.. choice.Requirements],
            Effects = effects,
            NoiseChange = choice.NoiseChange,
            NoiseSource = choice.NoiseSource,
            NoiseLocation = choice.NoiseLocation,
            CreatesDistraction = choice.CreatesDistraction,
            DistractionLocation = choice.DistractionLocation,
            DistractionStrength = choice.DistractionStrength,
            ThreatChange = choice.ThreatChange,
            TensionChange = choice.TensionChange,
            PlayerStyleEffects = new Dictionary<string, int>(choice.PlayerStyleEffects, StringComparer.OrdinalIgnoreCase),
            DirectorFlags = [.. choice.DirectorFlags],
            LockRollback = choice.LockRollback,
            IsCriticalTimed = choice.LockRollback || choice.IsTimed,
            IsDangerous = choice.ThreatChange > 0 || choice.NoiseChange > 10
        };
    }
}

public sealed class JsonCharacterRepository(
    string contentRoot,
    ILogger<JsonCharacterRepository> logger) : ICharacterRepository
{
    private readonly string _characterPath = SafePath.CombineWithin(contentRoot, "characters", "characters.tr.json");

    public async Task<IReadOnlyList<CharacterDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(_characterPath);
            return await JsonSerializer.DeserializeAsync<List<CharacterDefinition>>(stream, JsonDefaults.Options, cancellationToken)
                ?? [];
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Karakter verileri yüklenemedi.");
            throw;
        }
    }
}

public static class SafePath
{
    public static string CombineWithin(string root, params string[] parts)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("İzin verilen klasörün dışına çıkan dosya yolu reddedildi.");
        }
        return candidate;
    }

    public static string ValidateRelativeFile(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Geçersiz güncelleme yolu: {relativePath}");
        }
        return CombineWithin(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}

public sealed class LoggerDiagnostics(ILogger<LoggerDiagnostics> logger) : IGameDiagnostics
{
    public void Warning(string message) => logger.LogWarning("{GameWarning}", message);
    public void Error(string message) => logger.LogError("{GameError}", message);
}
