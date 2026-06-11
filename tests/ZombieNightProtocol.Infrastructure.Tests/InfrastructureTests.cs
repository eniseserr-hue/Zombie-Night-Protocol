using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ZombieNightProtocol.Core;
using ZombieNightProtocol.Infrastructure;

namespace ZombieNightProtocol.Infrastructure.Tests;

public sealed class InfrastructureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "znp-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task JsonStoryRepositoryLoadsValidStory()
    {
        var content = CreateContent();
        await File.WriteAllTextAsync(
            Path.Combine(content, "stories", "prototype-tr", "story.json"),
            JsonSerializer.Serialize(ValidStory(), JsonDefaults.Options));
        var repository = new JsonStoryRepository(content, NullLogger<JsonStoryRepository>.Instance);

        var story = await repository.LoadAsync();

        Assert.Equal("a", story.StartSceneId);
        Assert.Equal("images/scenes/test.webp", story.Scenes[0].SceneImage);
        Assert.Equal("images/scenes/choice.webp", story.Scenes[0].Choices[0].PreviewImage);
        Assert.Equal("Düşük", story.Scenes[0].Choices[0].RiskLevel);
        Assert.Equal("Kapı kontrol edilecek.", story.Scenes[0].Choices[0].SelectionSummary);
        Assert.Equal("Büşra", story.Scenes[0].TopMessages[0].Speaker);
    }

    [Fact]
    public async Task JsonStoryRepositoryLoadsVoiceMessageDefinition()
    {
        var content = CreateContent();
        var story = ValidStory();
        story.Scenes[0].VoiceMessage = new VoiceMessageDefinition
        {
            Id = "radio_01",
            SpeakerId = "bedo",
            Title = "Telsiz kaydı",
            AudioPath = "audio/voice/radio_01.mp3",
            Transcript = "Beni duyuyorsan merdivene inme.",
            RadioFilter = true,
            RememberAsListened = true
        };
        await File.WriteAllTextAsync(
            Path.Combine(content, "stories", "prototype-tr", "story.json"),
            JsonSerializer.Serialize(story, JsonDefaults.Options));
        var repository = new JsonStoryRepository(content, NullLogger<JsonStoryRepository>.Instance);

        var loaded = await repository.LoadAsync();

        var voiceMessage = loaded.Scenes[0].VoiceMessage;
        Assert.NotNull(voiceMessage);
        Assert.Equal("radio_01", voiceMessage.Id);
        Assert.Equal("audio/voice/radio_01.mp3", voiceMessage.AudioPath);
        Assert.True(voiceMessage.RadioFilter);
    }

    [Fact]
    public async Task JsonSeasonStoryRepositoryLoadsSeasonChapterScenesAndAdaptsToStoryPackage()
    {
        var content = CreateContent();
        await WriteSeasonFilesAsync(content);
        var repository = new JsonSeasonStoryRepository(content, NullLogger<JsonSeasonStoryRepository>.Instance);

        var season = await repository.LoadSeasonAsync();
        var chapter = await repository.LoadChapterAsync(season, season.StartChapterId);
        var scenes = await repository.LoadScenesAsync(season, chapter);
        var story = await repository.LoadAsStoryPackageAsync();

        Assert.Equal("season-01", season.Id);
        Assert.Equal("chapter-01", chapter.Id);
        Assert.Equal(2, scenes.Scenes.Count);
        Assert.Equal("scene-001", story.StartSceneId);
        Assert.Equal("images/scenes/season-01/chapter-01/scene-001.webp", story.Scenes[0].SceneImage);
        Assert.Equal("images/scenes/season-01/chapter-01/choice-001.webp", story.Scenes[0].Choices[0].PreviewImage);
        Assert.Equal(7, story.Scenes[0].DirectorRules.TensionChange);
        Assert.Equal("market", Assert.Single(story.Scenes[0].Tags));
        Assert.Equal("Depo", story.Scenes[0].Location);
        Assert.Equal("23:20", story.Scenes[0].TimeOfDay);
        Assert.Equal("Kalabalık", story.Scenes[0].ThreatHint);
        Assert.Equal(11, story.Scenes[0].NoiseBase);
        Assert.Equal(33, story.Scenes[0].InfectedDensityHint);
        Assert.Equal(2, story.Scenes[0].Choices[0].NoiseChange);
        Assert.Equal("distraction_throw", story.Scenes[0].Choices[0].NoiseSource);
        Assert.Equal("opposite_street", story.Scenes[0].Choices[0].DistractionLocation);
        Assert.True(story.Scenes[0].Choices[0].CreatesDistraction);
        Assert.Equal(35, story.Scenes[0].Choices[0].DistractionStrength);
        Assert.Equal(1, story.Scenes[0].Choices[0].ThreatChange);
        Assert.Equal(3, story.Scenes[0].Choices[0].TensionChange);
        Assert.Equal(2, story.Scenes[0].Choices[0].PlayerStyleEffects["Planli"]);
        Assert.Contains("saw_market_horde", story.Scenes[0].Choices[0].DirectorFlags);
        Assert.NotNull(story.Scenes[0].VoiceMessage);
        Assert.Equal("voice-001", story.Scenes[0].VoiceMessage!.Id);
    }

    [Fact]
    public async Task JsonStoryRepositoryKeepsLoadingLegacyPrototypeWhenSeasonFilesExist()
    {
        var content = CreateContent();
        await File.WriteAllTextAsync(
            Path.Combine(content, "stories", "prototype-tr", "story.json"),
            JsonSerializer.Serialize(ValidStory(), JsonDefaults.Options));
        await WriteSeasonFilesAsync(content);
        var repository = new JsonStoryRepository(content, NullLogger<JsonStoryRepository>.Instance);

        var story = await repository.LoadAsync();

        Assert.Equal("a", story.StartSceneId);
        Assert.Equal("prototype-tr", story.Id);
    }

    [Fact]
    public async Task JsonSeasonStoryRepositoryUsesDefaultsForMissingOptionalFields()
    {
        var content = CreateContent();
        await WriteSeasonFilesAsync(content, minimalScene: true);
        var repository = new JsonSeasonStoryRepository(content, NullLogger<JsonSeasonStoryRepository>.Instance);

        var story = await repository.LoadAsStoryPackageAsync();

        Assert.Equal("images/scenes/fallback.webp", story.Scenes[0].SceneImage);
        Assert.Empty(story.Scenes[0].Choices);
        Assert.Equal("22:45", story.Scenes[0].Time);
    }

    [Fact]
    public async Task JsonStoryRepositoryRejectsInvalidJson()
    {
        var content = CreateContent();
        await File.WriteAllTextAsync(Path.Combine(content, "stories", "prototype-tr", "story.json"), "{ invalid");
        var repository = new JsonStoryRepository(content, NullLogger<JsonStoryRepository>.Instance);

        await Assert.ThrowsAsync<JsonException>(() => repository.LoadAsync());
    }

    [Fact]
    public async Task SaveServiceWritesReadsAndBacksUpAtomically()
    {
        var paths = new ApplicationPaths(Path.Combine(_root, "appdata"));
        var service = new AtomicSaveService(paths, NullLogger<AtomicSaveService>.Instance);
        var state = State();

        await service.SaveAsync(1, state);
        state.Health = 75;
        await service.SaveAsync(1, state);
        var loaded = await service.LoadAsync(1);

        Assert.NotNull(loaded);
        Assert.Equal(75, loaded.Health);
        Assert.True(File.Exists(Path.Combine(paths.Saves, "slot-1.json.bak")));
    }

    [Fact]
    public async Task SaveServicePersistsCarryOverFields()
    {
        var paths = new ApplicationPaths(Path.Combine(_root, "appdata"));
        var service = new AtomicSaveService(paths, NullLogger<AtomicSaveService>.Instance);
        var state = State();
        state.PersistentChoices["promise_bedo"] = new PersistentChoiceState
        {
            Id = "promise_bedo",
            Label = "Bedo'ya söz",
            Value = "kept",
            RememberedByCharacters = ["bedo"],
            AffectsFutureChapters = true,
            AffectsFutureSeasons = true
        };
        state.CharacterMemories.Add(new CharacterMemoryState
        {
            CharacterId = "bedo",
            MemoryId = "saved_rooftop",
            SourceChoiceId = "save_bedo",
            Tone = "Trust",
            TrustImpact = 15,
            FutureMessageTags = ["gratitude"]
        });
        state.Promises.Add("promise_bedo");
        state.EnemyGroups.Add("raiders");
        state.AlliedGroups.Add("clinic");
        state.RouteSelections.Add("ankara_route");
        state.Vehicle.Id = "bus_01";
        state.Vehicle.Name = "Servis Otobüsü";
        state.Vehicle.Fuel = 42;

        await service.SaveAsync(1, state);
        var loaded = await service.LoadAsync(1);

        Assert.NotNull(loaded);
        Assert.Equal("kept", loaded.PersistentChoices["promise_bedo"].Value);
        Assert.Equal("saved_rooftop", Assert.Single(loaded.CharacterMemories).MemoryId);
        Assert.Contains("promise_bedo", loaded.Promises);
        Assert.Contains("raiders", loaded.EnemyGroups);
        Assert.Contains("clinic", loaded.AlliedGroups);
        Assert.Contains("ankara_route", loaded.RouteSelections);
        Assert.Equal("bus_01", loaded.Vehicle.Id);
    }

    [Fact]
    public async Task SaveServicePreservesNightDirectorState()
    {
        var paths = new ApplicationPaths(Path.Combine(_root, "appdata"));
        var service = new AtomicSaveService(paths, NullLogger<AtomicSaveService>.Instance);
        var state = State();
        state.Director.DirectorSeed = 987;
        state.Director.CurrentSeasonId = "season-01";
        state.Director.CurrentChapterId = "chapter-01";
        state.Director.CurrentSceneId = "scene-003";
        state.Director.SceneCounter = 12;
        state.Director.NoiseLevel = 44;
        state.Director.ThreatScore = 66;
        state.Director.ThreatLevel = ThreatLevel.High;
        state.Director.Flags.Add("saw_market_horde");
        state.Director.EventCooldowns["market_horde"] = 2;
        state.Director.ActiveNoiseSources.Add(new NoiseSourceState { Source = "alarm", Location = "street", Strength = 50, RemainingScenes = 3 });
        state.Director.RegionalNoiseLevels["street"] = 44;
        state.Director.NoiseHistory.Add(new NoiseHistoryEntry { Source = "alarm", Location = "street", Amount = 50, SceneIndex = 12 });
        state.Director.EventOccurrenceCounts["market_horde"] = 1;
        state.Director.RecentEvents.Add("market_horde");
        state.Director.RecoveryScenesRemaining = 2;
        state.Director.CurrentPacingState = PacingState.Recovery;
        state.Director.PlayerStyleScores["Silent"] = 2;
        state.Messages.Add(new CharacterMessageState
        {
            CharacterId = "bedo",
            CharacterName = "Bedo",
            Text = "Tamam kanka.",
            Tone = "Destekleyici",
            TrustLevel = "Yüksek",
            Mood = "Sakin",
            RelationshipContext = "Güveniyor",
            RelatedVoiceMessageId = "voice-01",
            Tags = ["team", "support"],
            SceneId = "scene-003"
        });

        await service.SaveAsync(1, state);
        var loaded = await service.LoadAsync(1);

        Assert.NotNull(loaded);
        Assert.Equal(987, loaded.Director.DirectorSeed);
        Assert.Equal("scene-003", loaded.Director.CurrentSceneId);
        Assert.Equal(12, loaded.Director.SceneCounter);
        Assert.Equal(44, loaded.Director.NoiseLevel);
        Assert.Equal(ThreatLevel.High, loaded.Director.ThreatLevel);
        Assert.Contains("saw_market_horde", loaded.Director.Flags);
        Assert.Equal(2, loaded.Director.EventCooldowns["market_horde"]);
        Assert.Equal("alarm", Assert.Single(loaded.Director.ActiveNoiseSources).Source);
        Assert.Equal(44, loaded.Director.RegionalNoiseLevels["street"]);
        Assert.Equal("alarm", Assert.Single(loaded.Director.NoiseHistory).Source);
        Assert.Equal(1, loaded.Director.EventOccurrenceCounts["market_horde"]);
        Assert.Contains("market_horde", loaded.Director.RecentEvents);
        Assert.Equal(2, loaded.Director.RecoveryScenesRemaining);
        Assert.Equal(PacingState.Recovery, loaded.Director.CurrentPacingState);
        Assert.Equal(2, loaded.Director.PlayerStyleScores["Silent"]);
        var message = Assert.Single(loaded.Messages);
        Assert.Equal("Yüksek", message.TrustLevel);
        Assert.Equal("Sakin", message.Mood);
        Assert.Equal("voice-01", message.RelatedVoiceMessageId);
        Assert.Contains("support", message.Tags);
    }

    [Fact]
    public async Task SaveServiceLoadsOldSaveWithoutNightDirector()
    {
        var paths = new ApplicationPaths(Path.Combine(_root, "appdata"));
        var legacyJson = """
            {
              "saveVersion": 1,
              "selectedCharacterId": "hero",
              "currentSceneId": "a",
              "currentChapterId": "chapter-01",
              "city": "İstanbul"
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(paths.Saves, "slot-1.json"), legacyJson, Encoding.UTF8);
        var service = new AtomicSaveService(paths, NullLogger<AtomicSaveService>.Instance);

        var loaded = await service.LoadAsync(1);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Director);
        Assert.Equal(0, loaded.Director.DirectorSeed);
        Assert.Equal(ThreatLevel.Low, loaded.Director.ThreatLevel);
    }

    [Fact]
    public async Task SaveServiceDetectsCorruptedSave()
    {
        var paths = new ApplicationPaths(Path.Combine(_root, "appdata"));
        await File.WriteAllTextAsync(Path.Combine(paths.Saves, "slot-1.json"), "not json");
        var service = new AtomicSaveService(paths, NullLogger<AtomicSaveService>.Instance);

        var loaded = await service.LoadAsync(1);
        var slots = await service.GetSlotsAsync();

        Assert.Null(loaded);
        Assert.True(slots[0].IsCorrupted);
    }

    [Fact]
    public async Task SaveServiceRecoversCorruptedPrimaryFromBackup()
    {
        var paths = new ApplicationPaths(Path.Combine(_root, "appdata"));
        var service = new AtomicSaveService(paths, NullLogger<AtomicSaveService>.Instance);
        var state = State();
        await service.SaveAsync(1, state);
        state.Health = 74;
        await service.SaveAsync(1, state);
        await File.WriteAllTextAsync(Path.Combine(paths.Saves, "slot-1.json"), "broken");

        var loaded = await service.LoadAsync(1);

        Assert.NotNull(loaded);
        Assert.Equal(100, loaded.Health);
    }

    [Fact]
    public async Task SettingsServiceReturnsDefaultsForCorruptedFile()
    {
        var paths = new ApplicationPaths(Path.Combine(_root, "appdata"));
        await File.WriteAllTextAsync(Path.Combine(paths.Settings, "settings.json"), "{oops");
        var service = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);

        var settings = await service.LoadAsync();

        Assert.Equal("Normal", settings.TextSpeed);
        Assert.True(settings.AutoSave);
    }

    [Fact]
    public async Task SettingsServicePersistsUserOptions()
    {
        var paths = new ApplicationPaths(Path.Combine(_root, "appdata"));
        var service = new JsonSettingsService(paths, NullLogger<JsonSettingsService>.Instance);
        var settings = new GameSettings
        {
            MasterVolume = 71,
            MusicVolume = 42,
            AmbientVolume = 33,
            EffectsVolume = 64,
            VoiceVolume = 58,
            NotificationVolume = 47,
            NotificationSounds = false,
            TextSpeed = "Hızlı",
            TextAnimationMode = "Kelime Kelime",
            TextAnimation = false,
            FullScreen = true,
            Resolution = "1600x900",
            UiScale = 110,
            AutoSave = false,
            TopMessageNotifications = false,
            Language = "Türkçe"
        };

        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();

        Assert.Equal(71, loaded.MasterVolume);
        Assert.Equal(42, loaded.MusicVolume);
        Assert.Equal(33, loaded.AmbientVolume);
        Assert.Equal(64, loaded.EffectsVolume);
        Assert.Equal(58, loaded.VoiceVolume);
        Assert.Equal(47, loaded.NotificationVolume);
        Assert.False(loaded.NotificationSounds);
        Assert.Equal("Hızlı", loaded.TextSpeed);
        Assert.Equal("Kelime Kelime", loaded.TextAnimationMode);
        Assert.False(loaded.TextAnimation);
        Assert.True(loaded.FullScreen);
        Assert.Equal("1600x900", loaded.Resolution);
        Assert.Equal(110, loaded.UiScale);
        Assert.False(loaded.AutoSave);
        Assert.False(loaded.TopMessageNotifications);
        Assert.Equal("Türkçe", loaded.Language);
    }

    [Fact]
    public async Task HashServiceVerifiesSha256()
    {
        var path = Path.Combine(_root, "hash.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "zombie", Encoding.UTF8);
        var hash = await HashService.Sha256Async(path);

        Assert.True(await HashService.VerifyAsync(path, hash));
        Assert.False(await HashService.VerifyAsync(path, new string('0', 64)));
    }

    [Fact]
    public async Task UpdatePlannerFindsChangedAndDeletedFiles()
    {
        var app = Path.Combine(_root, "app");
        Directory.CreateDirectory(app);
        await File.WriteAllTextAsync(Path.Combine(app, "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(app, "old.txt"), "old");
        var sameHash = await HashService.Sha256Async(Path.Combine(app, "same.txt"));
        var manifest = new UpdateManifest
        {
            Files =
            [
                new UpdateFileEntry { Path = "same.txt", Sha256 = sameHash },
                new UpdateFileEntry { Path = "changed.txt", Sha256 = new string('1', 64) },
                new UpdateFileEntry { Path = "old.txt", Delete = true }
            ]
        };

        var plan = await new UpdatePlanner().CreateAsync(app, manifest);

        Assert.Single(plan.Download);
        Assert.Equal("changed.txt", plan.Download[0].Path);
        Assert.Single(plan.Delete);
        Assert.Equal("old.txt", plan.Delete[0].Path);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:/outside.txt")]
    public void SafePathRejectsTraversal(string path)
    {
        Directory.CreateDirectory(_root);
        Assert.Throws<InvalidDataException>(() => SafePath.ValidateRelativeFile(_root, path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateContent()
    {
        var content = Path.Combine(_root, "content");
        Directory.CreateDirectory(Path.Combine(content, "stories", "prototype-tr"));
        return content;
    }

    private static async Task WriteSeasonFilesAsync(string content, bool minimalScene = false)
    {
        var seasonRoot = Path.Combine(content, "stories", "tr", "season-01");
        var chapterRoot = Path.Combine(seasonRoot, "chapter-01");
        Directory.CreateDirectory(chapterRoot);
        await File.WriteAllTextAsync(
            Path.Combine(seasonRoot, "season.json"),
            JsonSerializer.Serialize(new SeasonDefinition
            {
                Id = "season-01",
                Title = "Sezon 1",
                Description = "Test sezonu",
                Order = 1,
                Language = "tr",
                StartChapterId = "chapter-01",
                Chapters =
                [
                    new SeasonChapterReference
                    {
                        Id = "chapter-01",
                        Title = "Bölüm 1",
                        Order = 1,
                        ChapterPath = "chapter-01/chapter.json"
                    }
                ]
            }, JsonDefaults.Options));
        await File.WriteAllTextAsync(
            Path.Combine(chapterRoot, "chapter.json"),
            JsonSerializer.Serialize(new ChapterDefinition
            {
                Id = "chapter-01",
                SeasonId = "season-01",
                Title = "Bölüm 1",
                Order = 1,
                StartSceneId = "scene-001",
                SceneFile = "scenes.json"
            }, JsonDefaults.Options));

        var document = minimalScene
            ? new SeasonalScenesDocument
            {
                Scenes =
                [
                    new SeasonalScene
                    {
                        Id = "scene-001",
                        ChapterId = "chapter-01",
                        Title = "Minimal",
                        Text = "Eksik opsiyonel alan testi."
                    }
                ]
            }
            : new SeasonalScenesDocument
            {
                Scenes =
                [
                    new SeasonalScene
                    {
                        Id = "scene-001",
                        ChapterId = "chapter-01",
                        Title = "Başlangıç",
                        Text = "Test sahnesi.",
                        SceneImage = "images/scenes/season-01/chapter-01/scene-001.webp",
                        DirectorRules = new DirectorRules { TensionChange = 7 },
                        Tags = ["market"],
                        Location = "Depo",
                        TimeOfDay = "23:20",
                        ThreatHint = "Kalabalık",
                        NoiseBase = 11,
                        InfectedDensityHint = 33,
                        VoiceMessage = new VoiceMessageDefinition
                        {
                            Id = "voice-001",
                            Title = "Ses kaydı",
                            Transcript = "Test transcript"
                        },
                        Choices =
                        [
                            new SeasonalChoice
                            {
                                Id = "choice-001",
                                Text = "Devam et",
                                TargetSceneId = "scene-002",
                                ChoicePreviewImage = "images/scenes/season-01/chapter-01/choice-001.webp",
                                Summary = "Devam eder.",
                                PersistentChoiceId = "continued",
                                NoiseChange = 2,
                                NoiseSource = "distraction_throw",
                                CreatesDistraction = true,
                                DistractionLocation = "opposite_street",
                                DistractionStrength = 35,
                                ThreatChange = 1,
                                TensionChange = 3,
                                PlayerStyleEffects = { ["Planli"] = 2 },
                                DirectorFlags = ["saw_market_horde"]
                            }
                        ]
                    },
                    new SeasonalScene
                    {
                        Id = "scene-002",
                        ChapterId = "chapter-01",
                        Title = "Son",
                        Text = "Bitti."
                    }
                ]
            };
        await File.WriteAllTextAsync(Path.Combine(chapterRoot, "scenes.json"), JsonSerializer.Serialize(document, JsonDefaults.Options));
    }

    private static StoryPackage ValidStory() => new()
    {
        StartSceneId = "a",
        Scenes =
        [
            new StoryScene
            {
                Id = "a",
                Title = "A",
                SceneImage = "images/scenes/test.webp",
                TopMessages = [new TopMessageDefinition { Speaker = "Büşra", Text = "Sessiz ol.", Tone = "Uyarı" }],
                Content = [new StoryBlock { Text = "Test" }],
                Choices =
                [
                    new StoryChoice
                    {
                        Id = "choice",
                        Text = "Git",
                        PreviewImage = "images/scenes/choice.webp",
                        SelectionSummary = "Kapı kontrol edilecek.",
                        RiskLevel = "Düşük"
                    }
                ]
            }
        ]
    };

    private static GameState State() => new()
    {
        SelectedCharacterId = "hero",
        CurrentSceneId = "a",
        Characters = { new CharacterState { Id = "hero", Name = "Hero", IsPlayer = true } }
    };
}
