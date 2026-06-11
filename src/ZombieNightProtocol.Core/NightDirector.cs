using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZombieNightProtocol.Core;

public sealed record DirectorEventDefinition(
    string Id,
    string Description,
    int MinimumTension,
    int ThreatDelta,
    int NoiseDelta,
    int CooldownChoices,
    bool IsMajor);

public sealed record DirectorEventResult(string EventId, string Description, bool IsMajor);

public sealed class NightDirectorService(IGameDiagnostics? diagnostics = null)
{
    private const int RecentEventLimit = 6;
    private const int NoiseHistoryLimit = 12;
    private readonly IGameDiagnostics _diagnostics = diagnostics ?? new NullGameDiagnostics();
    private readonly List<DirectorEvent> _events = [];
    private static readonly JsonSerializerOptions EventJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private NightDirectorState _state = new();
    private StoryScene? _currentScene;

    public static IReadOnlyDictionary<string, int> DefaultNoiseStrengths { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["gunshot"] = 88,
            ["door_break"] = 46,
            ["vehicle_engine"] = 68,
            ["alarm"] = 92,
            ["shouting"] = 32,
            ["glass_break"] = 42,
            ["metal_drop"] = 34,
            ["running"] = 22,
            ["generator"] = 58,
            ["speaker"] = 64,
            ["radio_broadcast"] = 38,
            ["infected_door_hit"] = 44,
            ["distraction_throw"] = 30
        };

    public void Initialize(int seed, string seasonId, string chapterId)
    {
        _state = new NightDirectorState
        {
            DirectorSeed = seed == 0 ? StableHash(seasonId, chapterId, "night-director") : seed,
            CurrentSeasonId = seasonId,
            CurrentChapterId = chapterId
        };
        ApplyNightModifiers();
        CalculateThreatScore();
        RecalculateTension();
    }

    public void LoadState(NightDirectorState? state)
    {
        try
        {
            _state = state is null ? new NightDirectorState() : CloneState(state);
            if (_state.DirectorSeed == 0)
            {
                _state.DirectorSeed = StableHash(_state.CurrentSeasonId, _state.CurrentChapterId, "loaded");
            }
            ApplyNightModifiers();
            CalculateThreatScore();
            RecalculateTension();
        }
        catch (Exception exception)
        {
            _diagnostics.Warning($"Night Director state yüklenemedi; güvenli varsayılan kullanılacak. {exception.Message}");
            _state = new NightDirectorState();
        }
    }

    public NightDirectorState GetState() => _state;

    public void LoadDirectorEvents(IEnumerable<DirectorEvent> events)
    {
        _events.Clear();
        _events.AddRange(events.Where(item => !string.IsNullOrWhiteSpace(item.Id)));
    }

    public void LoadDirectorEvents(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _diagnostics.Warning($"Director event dosyası bulunamadı: {path}");
                return;
            }
            using var stream = File.OpenRead(path);
            var events = JsonSerializer.Deserialize<List<DirectorEvent>>(stream, EventJsonOptions) ?? [];
            LoadDirectorEvents(events);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _diagnostics.Warning($"Director event dosyası yüklenemedi: {exception.Message}");
        }
    }

    public void OnSceneEntered(StoryScene scene)
    {
        _currentScene = scene;
        _state.SceneCounter++;
        _state.ScenesSinceLastMajorEvent = _state.LastMajorEventSceneIndex < 0 ? _state.SceneCounter : _state.SceneCounter - _state.LastMajorEventSceneIndex;
        _state.ScenesSinceLastRest++;
        _state.CurrentSceneId = scene.Id;
        _state.CurrentChapterId = scene.ChapterId;
        _state.IsNight = scene.IsNight || IsNightTime(scene.TimeOfDay) || IsNightTime(scene.Time);
        _state.CurrentLocation = scene.Location;
        _state.RegionTags.Clear();
        _state.RegionTags.AddRange(scene.Tags);
        _state.NearbyInfectedEstimate = Math.Clamp(scene.InfectedDensityHint, 0, 100);
        ApplySceneThreatContext(scene);
        DecayCooldowns();
        DecayEventPressure();
        DecayNoise();
        CalculateThreatScore();
        RecalculateTension();
    }

    public void ApplyChoiceEffects(StoryChoice choice) => ApplyChoiceNoiseAndThreat(choice);

    public void ApplyChoiceNoiseAndThreat(StoryChoice choice)
    {
        var source = string.IsNullOrWhiteSpace(choice.NoiseSource) ? choice.Id : choice.NoiseSource;
        var location = string.IsNullOrWhiteSpace(choice.NoiseLocation) ? _state.CurrentLocation : choice.NoiseLocation;
        if (choice.NoiseChange != 0)
        {
            AddNoise(source, choice.NoiseChange, location);
        }
        if (choice.CreatesDistraction)
        {
            CreateDistraction(
                string.IsNullOrWhiteSpace(choice.DistractionLocation) ? location : choice.DistractionLocation,
                choice.DistractionStrength == 0 ? Math.Max(choice.NoiseChange, DefaultNoiseStrengths["distraction_throw"]) : choice.DistractionStrength);
        }
        ApplyThreatDelta(choice.ThreatChange);
        ApplyTensionDelta(choice.TensionChange, choice.Id);
        foreach (var effect in choice.Effects)
        {
            if (effect.Type.Equals("ChangeNoise", StringComparison.OrdinalIgnoreCase))
            {
                AddNoise(source, effect.Amount, location);
            }
            else if (effect.Type.Equals("ChangeThreat", StringComparison.OrdinalIgnoreCase))
            {
                ApplyThreatDelta(effect.Amount);
            }
            else if (effect.Type.Equals("ChangeTension", StringComparison.OrdinalIgnoreCase))
            {
                ApplyTensionDelta(effect.Amount, choice.Id);
            }
        }
        if (choice.IsDangerous)
        {
            _state.PlayerStyleScores["Risk"] = _state.PlayerStyleScores.GetValueOrDefault("Risk") + 1;
        }
        foreach (var playerStyle in choice.PlayerStyleEffects)
        {
            _state.PlayerStyleScores[playerStyle.Key] = _state.PlayerStyleScores.GetValueOrDefault(playerStyle.Key) + playerStyle.Value;
        }
        foreach (var flag in choice.DirectorFlags)
        {
            _state.Flags.Add(flag);
        }
        CalculateThreatScore();
        RecalculateTension();
    }

    public void UpdateNoise(int delta, string source = "") =>
        AddNoise(string.IsNullOrWhiteSpace(source) ? "unknown" : source, delta, _state.CurrentLocation);

    public void AddNoise(string source, int amount, string location)
    {
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
        var normalizedLocation = string.IsNullOrWhiteSpace(location) ? _state.CurrentLocation : location;
        var sourceBase = DefaultNoiseStrengths.GetValueOrDefault(normalizedSource, Math.Abs(amount));
        var effectiveAmount = amount == 0 ? sourceBase : amount;
        if (_state.IsNight)
        {
            effectiveAmount = (int)Math.Round(effectiveAmount * _state.NightNoiseSensitivityMultiplier);
        }

        _state.NoiseLevel = Clamp(_state.NoiseLevel + effectiveAmount);
        if (IsCurrentLocation(normalizedLocation))
        {
            _state.LocalNoiseLevel = Clamp(_state.LocalNoiseLevel + Math.Max(0, effectiveAmount));
        }
        AddRegionalNoise(normalizedLocation, normalizedSource, effectiveAmount);
        RegisterActiveNoise(normalizedSource, normalizedLocation, Math.Max(sourceBase, Math.Abs(effectiveAmount)));
        _state.LastNoiseSource = normalizedSource;
        _state.LastNoiseLocation = normalizedLocation;
        _state.NoiseLocation = normalizedLocation;
        _state.LastNoiseTimestamp = DateTimeOffset.UtcNow;
        AddNoiseHistory(normalizedSource, normalizedLocation, effectiveAmount);
        CalculateThreatScore();
        RecalculateTension();
    }

    public void AddRegionalNoise(string regionId, string source, int amount)
    {
        var region = string.IsNullOrWhiteSpace(regionId) ? "unknown" : regionId;
        _state.RegionalNoiseLevels[region] = Clamp(_state.RegionalNoiseLevels.GetValueOrDefault(region) + amount);
    }

    public void CreateDistraction(string location, int strength)
    {
        var target = string.IsNullOrWhiteSpace(location) ? "distraction" : location;
        AddRegionalNoise(target, "distraction_throw", strength);
        RegisterActiveNoise("distraction_throw", target, strength);
        AddNoiseHistory("distraction_throw", target, strength);
        if (!IsCurrentLocation(target))
        {
            _state.LocalNoiseLevel = Math.Max(0, _state.LocalNoiseLevel - Math.Max(4, strength / 4));
            ApplyThreatDelta(-Math.Max(2, strength / 8));
        }
        CalculateThreatScore();
        RecalculateTension();
    }

    public void DecayNoise()
    {
        var decay = Math.Max(1, _state.NoiseDecayRate);
        _state.NoiseLevel = Math.Max(0, _state.NoiseLevel - (_state.IsNight ? Math.Max(1, decay / 2) : decay));
        _state.LocalNoiseLevel = Math.Max(0, _state.LocalNoiseLevel - (decay * 2));
        foreach (var key in _state.RegionalNoiseLevels.Keys.ToList())
        {
            _state.RegionalNoiseLevels[key] = Math.Max(0, _state.RegionalNoiseLevels[key] - decay);
            if (_state.RegionalNoiseLevels[key] == 0)
            {
                _state.RegionalNoiseLevels.Remove(key);
            }
        }
        for (var index = _state.ActiveNoiseSources.Count - 1; index >= 0; index--)
        {
            var source = _state.ActiveNoiseSources[index];
            source.Strength = Math.Max(0, source.Strength - decay);
            source.RemainingScenes--;
            if (source.Strength == 0 || source.RemainingScenes <= 0)
            {
                _state.ActiveNoiseSources.RemoveAt(index);
            }
        }
    }

    public int CalculateThreatScore()
    {
        var noisePressure = (int)Math.Round((_state.NoiseLevel * 0.34) + (_state.LocalNoiseLevel * 0.42));
        if (_state.IsNight)
        {
            noisePressure = (int)Math.Round(noisePressure * _state.NightNoiseSensitivityMultiplier);
        }
        var infectedPressure = _state.NearbyInfectedEstimate / 3;
        var locationPressure = CalculateLocationPressure();
        var longNoisePressure = _state.ActiveNoiseSources
            .Where(source => IsLongNoiseSource(source.Source))
            .Sum(source => Math.Max(1, source.Strength / 12));
        var stayPressure = Math.Min(10, _state.SceneCounter / 4);
        var flagPressure = _state.Flags.Contains("night_danger_ready") ? 4 : 0;
        var manualThreat = _state.Variables.GetValueOrDefault("threatAdjustment");
        var score = noisePressure + infectedPressure + locationPressure + longNoisePressure + stayPressure + flagPressure + manualThreat;
        if (_state.IsNight)
        {
            score = (int)Math.Round(score * _state.NightThreatMultiplier);
        }
        _state.ThreatScore = Clamp(score);
        _state.ThreatLevel = GetThreatLevel(_state.ThreatScore);
        _state.ThreatBand = _state.ThreatLevel.ToString();
        return _state.ThreatScore;
    }

    public void RecalculateThreat() => CalculateThreatScore();

    public ThreatLevel GetThreatLevel(int score) => ScoreToThreatLevel(score);

    public void RecalculateTension()
    {
        var resourcePressure = _state.Flags.Contains("low_resources") ? 8 : 0;
        var injuryPressure = _state.Flags.Contains("party_injured") ? 8 : 0;
        var unknownPressure = _state.RegionTags.Contains("unknown", StringComparer.OrdinalIgnoreCase) ? 5 : 0;
        var longNoisePressure = _state.ActiveNoiseSources.Any(source => IsLongNoiseSource(source.Source)) ? 7 : 0;
        var calmPressure = _state.SceneCounter - _state.LastCalmSceneIndex > 4 ? 6 : 0;
        var manualTension = _state.Variables.GetValueOrDefault("tensionAdjustment");
        var score = (_state.ThreatScore / 2) + (_state.NoiseLevel / 5) + (_state.LocalNoiseLevel / 4)
            + (_state.IsNight ? 8 : 0) + resourcePressure + injuryPressure + unknownPressure + longNoisePressure + calmPressure + manualTension;
        if (_state.RecoveryScenesRemaining > 0)
        {
            score -= 12;
        }
        _state.TensionLevel = Clamp(score);
        UpdatePacingState();
    }

    public void ApplyTensionDelta(int amount, string reason)
    {
        _state.Variables["tensionAdjustment"] = ClampSigned(_state.Variables.GetValueOrDefault("tensionAdjustment") + amount);
        _state.RecentTensionChanges.Add(amount);
        if (_state.RecentTensionChanges.Count > 8)
        {
            _state.RecentTensionChanges.RemoveAt(0);
        }
        if (amount < 0)
        {
            _state.LastCalmSceneIndex = _state.SceneCounter;
        }
        RecalculateTension();
    }

    public PacingState GetPacingState() => _state.CurrentPacingState;

    public void StartRecovery(int sceneCount)
    {
        _state.RecoveryScenesRemaining = Math.Max(_state.RecoveryScenesRemaining, sceneCount);
        _state.CurrentPacingState = PacingState.Recovery;
    }

    public bool CanTriggerMajorEvent() =>
        _state.MajorEventCooldown <= 0 && _state.RecoveryScenesRemaining <= 0 && _state.TensionLevel >= 45;

    public bool CanTriggerMinorEvent() =>
        _state.MinorEventCooldown <= 0 && _state.TensionLevel >= 15;

    public void OnMajorEventTriggered(string eventId)
    {
        RegisterEventOccurrence(eventId);
        _state.LastMajorEventSceneIndex = _state.SceneCounter;
        _state.ScenesSinceLastMajorEvent = 0;
        _state.MajorEventCooldown = 3;
        StartRecovery(2);
    }

    public void OnMinorEventTriggered(string eventId)
    {
        RegisterEventOccurrence(eventId);
        _state.MinorEventCooldown = 1;
    }

    public string GetThreatDescription() => ThreatDescription(_state.ThreatLevel);

    public void ApplyNightModifiers()
    {
        var settings = new NightDirectorSettings();
        _state.NightThreatMultiplier = _state.IsNight ? settings.NightThreatMultiplier : 1;
        _state.NightNoiseSensitivityMultiplier = _state.IsNight ? settings.NightNoiseSensitivityMultiplier : 1;
        _state.NightEventWeightMultiplier = settings.NightEventWeightMultiplier;
        if (_state.IsNight)
        {
            _state.Flags.Add("night_danger_ready");
        }
    }

    public void ApplySceneThreatContext(StoryScene scene)
    {
        _state.IsNight = scene.IsNight || IsNightTime(scene.TimeOfDay) || IsNightTime(scene.Time);
        ApplyNightModifiers();
        if (scene.NoiseBase > 0)
        {
            _state.NoiseLevel = Math.Max(_state.NoiseLevel, Clamp(scene.NoiseBase));
        }
        if (scene.DirectorRules.TensionChange != 0)
        {
            ApplyTensionDelta(scene.DirectorRules.TensionChange, scene.Id);
        }
        if (scene.DirectorRules.ForceCalm)
        {
            ApplyTensionDelta(-18, scene.Id);
            _state.LastCalmSceneIndex = _state.SceneCounter;
        }
        if (!string.IsNullOrWhiteSpace(scene.ThreatHint))
        {
            _state.Variables["sceneThreatHint"] = scene.ThreatHint.ToLowerInvariant() switch
            {
                var value when value.Contains("kalabal") || value.Contains("high") => 12,
                var value when value.Contains("orta") || value.Contains("medium") => 7,
                var value when value.Contains("düşük") || value.Contains("low") => 3,
                _ => 0
            };
        }
        if (scene.LightLevel < 30 || scene.LightingHint.Contains("dark", StringComparison.OrdinalIgnoreCase))
        {
            _state.Variables["lowLight"] = 5;
        }
    }

    public IReadOnlyList<DirectorEvent> GetEligibleEvents(StoryScene scene, NightDirectorState? state = null)
    {
        var candidateState = state is null ? _state : CloneState(state);
        if (!scene.DirectorRules.AllowDirectorEvents)
        {
            return [];
        }
        return _events.Where(item => IsEventEligible(item, scene, candidateState)).ToList();
    }

    public int CalculateEventWeight(DirectorEvent directorEvent, NightDirectorState? state = null)
    {
        var candidateState = state ?? _state;
        var weight = Math.Max(0, directorEvent.Weight);
        if (weight == 0)
        {
            return 0;
        }
        if (directorEvent.IsMajor && candidateState.RecoveryScenesRemaining > 0)
        {
            weight = Math.Max(1, weight / 4);
        }
        if (directorEvent.IsMajor && !CanTriggerMajorEvent())
        {
            weight = Math.Max(1, weight / 5);
        }
        if (directorEvent.IsMinor && candidateState.TensionLevel < 25 && candidateState.SceneCounter - candidateState.LastCalmSceneIndex > 3)
        {
            weight += 8;
        }
        if (candidateState.IsNight)
        {
            weight = (int)Math.Round(weight * candidateState.NightEventWeightMultiplier);
        }
        if (candidateState.RecentEvents.Contains(directorEvent.Id, StringComparer.OrdinalIgnoreCase))
        {
            weight = Math.Max(1, weight / 3);
        }
        if (_currentScene is not null)
        {
            weight = (int)Math.Round(weight * Math.Max(0.1, _currentScene.DirectorRules.EventWeightMultiplier));
        }
        return weight;
    }

    public DirectorEvent? ChooseEventDeterministically(StoryScene scene, NightDirectorState? state = null)
    {
        var eligible = GetEligibleEvents(scene, state);
        if (eligible.Count == 0)
        {
            return null;
        }
        var weighted = eligible
            .Select(item => new { Event = item, Weight = CalculateEventWeight(item, state) })
            .Where(item => item.Weight > 0)
            .ToList();
        var total = weighted.Sum(item => item.Weight);
        if (total <= 0)
        {
            return null;
        }
        var roll = CreateDeterministicRandom($"event:{scene.Id}:{_state.RecentEvents.Count}").Next(0, total);
        var cursor = 0;
        foreach (var item in weighted)
        {
            cursor += item.Weight;
            if (roll < cursor)
            {
                return item.Event;
            }
        }
        return weighted[^1].Event;
    }

    public DirectorEventResult ApplyDirectorEvent(DirectorEvent directorEvent)
    {
        foreach (var effect in directorEvent.Effects)
        {
            ApplyDirectorEventEffect(effect);
        }
        if (directorEvent.IsMajor)
        {
            OnMajorEventTriggered(directorEvent.Id);
        }
        else
        {
            OnMinorEventTriggered(directorEvent.Id);
        }
        var text = !string.IsNullOrWhiteSpace(directorEvent.EventText)
            ? directorEvent.EventText
            : directorEvent.Description;
        CalculateThreatScore();
        RecalculateTension();
        return new DirectorEventResult(directorEvent.Id, text, directorEvent.IsMajor);
    }

    public void RegisterEventOccurrence(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return;
        }
        _state.LastEventId = eventId;
        _state.EventOccurrenceCounts[eventId] = _state.EventOccurrenceCounts.GetValueOrDefault(eventId) + 1;
        var definition = _events.FirstOrDefault(item => item.Id.Equals(eventId, StringComparison.OrdinalIgnoreCase));
        _state.EventCooldowns[eventId] = definition?.CooldownScenes ?? 3;
        _state.RecentEvents.Add(eventId);
        while (_state.RecentEvents.Count > RecentEventLimit)
        {
            _state.RecentEvents.RemoveAt(0);
        }
    }

    public bool IsEventOnCooldown(string eventId) =>
        _state.EventCooldowns.GetValueOrDefault(eventId) > 0;

    public IReadOnlyList<string> GetRecentEvents() => [.. _state.RecentEvents];

    public IReadOnlyList<string> GetDominantPlayerStyles(int limit = 3) =>
        _state.PlayerStyleScores
            .Where(pair => pair.Value != 0)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .Select(pair => pair.Key)
            .ToList();

    public CharacterHelpDecision DecideCharacterHelp(
        GameState gameState,
        CharacterDefinition definition,
        CharacterState helper,
        RelationshipState? relationship,
        CharacterHelpRequest request)
    {
        var trust = relationship?.Trust ?? 50;
        var trustModifier = (trust - 50) / 2 + PlayerStyleTrustBias(_state.PlayerStyleScores);
        var moodModifier = MoodModifier(helper.Mood);
        var injuryModifier = helper.Injury.Equals("Yok", StringComparison.OrdinalIgnoreCase) ? 0 : -22;
        var fatigueModifier = -(gameState.Fatigue / 4);
        var hungerThirstModifier = -((gameState.Hunger + gameState.Thirst) / 12);
        var fearModifier = definition.Fear.Contains(request.RequestType, StringComparison.OrdinalIgnoreCase) ? -12 : 0;
        var skillModifier = !string.IsNullOrWhiteSpace(request.RequiredSkill) && definition.Stats.TryGetValue(request.RequiredSkill, out var skill)
            ? (skill - request.Difficulty) / 2
            : 0;
        var specialtyModifier = MatchesSpecialty(definition, request) ? 12 : 0;
        var itemModifier = string.IsNullOrWhiteSpace(request.RequiredItem) || gameState.Inventory.Any(item => item.Id.Equals(request.RequiredItem, StringComparison.OrdinalIgnoreCase))
            ? 0
            : -20;
        var helpedBeforeModifier = gameState.CharacterMemories.Any(memory => memory.CharacterId.Equals(helper.Id, StringComparison.OrdinalIgnoreCase) && memory.Tone.Contains("Trust", StringComparison.OrdinalIgnoreCase))
            ? 8
            : 0;
        var dangerPenalty = -(request.DangerLevel / 4);
        var directorPenalty = -((_state.ThreatScore + _state.TensionLevel) / 12);
        var styleModifier = _state.PlayerStyleScores.GetValueOrDefault("Loyal") / 2
            + _state.PlayerStyleScores.GetValueOrDefault("Protective") / 3
            - _state.PlayerStyleScores.GetValueOrDefault("Selfish") / 2
            - _state.PlayerStyleScores.GetValueOrDefault("Untrustworthy");
        var score = 55 + trustModifier + moodModifier + injuryModifier + fatigueModifier + hungerThirstModifier
            + fearModifier + skillModifier + specialtyModifier + itemModifier + helpedBeforeModifier + dangerPenalty + directorPenalty + styleModifier;
        var result = score switch
        {
            >= 72 => CharacterHelpResult.Accept,
            >= 58 => CharacterHelpResult.ReluctantAccept,
            >= 48 => CharacterHelpResult.ConditionalAccept,
            >= 36 => CharacterHelpResult.SuggestAlternative,
            >= 20 => CharacterHelpResult.Refuse,
            _ => CharacterHelpResult.AngryRefuse
        };
        return new CharacterHelpDecision
        {
            CharacterId = helper.Id,
            RequestType = request.RequestType,
            Difficulty = request.Difficulty,
            DangerLevel = request.DangerLevel,
            RequiredSkill = request.RequiredSkill,
            RequiredItem = request.RequiredItem,
            TrustModifier = trustModifier,
            MoodModifier = moodModifier,
            InjuryModifier = injuryModifier,
            FatigueModifier = fatigueModifier + hungerThirstModifier,
            FearModifier = fearModifier,
            Result = result,
            Score = Math.Clamp(score, 0, 100),
            Message = BuildHelpMessage(definition, helper, result, relationship?.Trust ?? 50, request),
            Effects = BuildHelpEffects(helper.Id, result)
        };
    }

    public CharacterMessageState CreateTeamMessage(
        CharacterDefinition definition,
        CharacterState character,
        RelationshipState? relationship,
        string sceneId,
        int day,
        string gameTime,
        string topic = "")
    {
        var trust = relationship?.Trust ?? 50;
        var tone = trust switch
        {
            >= 75 => "Destekleyici",
            >= 45 => "Temkinli",
            >= 20 => "Mesafeli",
            _ => "Sert"
        };
        var text = CharacterVoiceLine(definition.Id, trust, character.Mood, topic);
        return new CharacterMessageState
        {
            CharacterId = character.Id,
            CharacterName = character.Name,
            Text = text,
            Tone = tone,
            TrustLevel = TrustLabel(trust),
            Mood = character.Mood,
            RelationshipContext = relationship?.Status ?? "Temkinli",
            Day = day,
            GameTime = gameTime,
            SceneId = sceneId,
            Tags = ["team", tone.ToLowerInvariant()]
        };
    }

    public Random CreateDeterministicRandom(string salt = "") =>
        new(StableHash(_state.DirectorSeed.ToString(), _state.SceneCounter.ToString(), _state.CurrentSceneId, salt));

    public NightDirectorState SaveStateSnapshot() => CloneState(_state);

    public static ThreatLevel ScoreToThreatLevel(int score) => Math.Clamp(score, 0, 100) switch
    {
        <= 19 => ThreatLevel.Safe,
        <= 39 => ThreatLevel.Low,
        <= 59 => ThreatLevel.Medium,
        <= 79 => ThreatLevel.High,
        _ => ThreatLevel.Critical
    };

    public static string ThreatDescription(ThreatLevel level) => level switch
    {
        ThreatLevel.Safe => "Sokak şimdilik sakin.",
        ThreatLevel.Low => "Yakınlarda hareket var.",
        ThreatLevel.Medium => "Sesiniz çevreye yayılıyor.",
        ThreatLevel.High => "Burada fazla kalamazsınız.",
        ThreatLevel.Critical => "Sürü konumunuzu biliyor.",
        _ => "Yakınlarda hareket var."
    };

    public static NightDirectorState CloneState(NightDirectorState source)
    {
        var clone = new NightDirectorState
        {
            DirectorSeed = source.DirectorSeed,
            CurrentSceneId = source.CurrentSceneId,
            CurrentSeasonId = source.CurrentSeasonId,
            CurrentChapterId = source.CurrentChapterId,
            SceneCounter = source.SceneCounter,
            LastMajorEventSceneIndex = source.LastMajorEventSceneIndex,
            LastCalmSceneIndex = source.LastCalmSceneIndex,
            ScenesSinceLastMajorEvent = source.ScenesSinceLastMajorEvent,
            ScenesSinceLastRest = source.ScenesSinceLastRest,
            TensionLevel = source.TensionLevel,
            CurrentPacingState = source.CurrentPacingState,
            PressureBudget = source.PressureBudget,
            RecoveryScenesRemaining = source.RecoveryScenesRemaining,
            MajorEventCooldown = source.MajorEventCooldown,
            MinorEventCooldown = source.MinorEventCooldown,
            NoiseLevel = source.NoiseLevel,
            LocalNoiseLevel = source.LocalNoiseLevel,
            ThreatScore = source.ThreatScore,
            ThreatLevel = source.ThreatLevel,
            IsNight = source.IsNight,
            CurrentLocation = source.CurrentLocation,
            NearbyInfectedEstimate = source.NearbyInfectedEstimate,
            LastNoiseSource = source.LastNoiseSource,
            LastNoiseLocation = source.LastNoiseLocation,
            LastNoiseTimestamp = source.LastNoiseTimestamp,
            NoiseDecayRate = source.NoiseDecayRate,
            NoiseLocation = source.NoiseLocation,
            ThreatBand = source.ThreatBand,
            NightThreatMultiplier = source.NightThreatMultiplier,
            NightNoiseSensitivityMultiplier = source.NightNoiseSensitivityMultiplier,
            NightEventWeightMultiplier = source.NightEventWeightMultiplier,
            LastMajorEventSceneId = source.LastMajorEventSceneId,
            LastEventId = source.LastEventId
        };
        clone.RegionTags.AddRange(source.RegionTags);
        clone.RecentTensionChanges.AddRange(source.RecentTensionChanges);
        clone.ActiveNoiseSources.AddRange(source.ActiveNoiseSources.Select(item => new NoiseSourceState
        {
            Source = item.Source,
            Location = item.Location,
            Strength = item.Strength,
            RemainingScenes = item.RemainingScenes
        }));
        clone.NoiseHistory.AddRange(source.NoiseHistory.Select(item => new NoiseHistoryEntry
        {
            Source = item.Source,
            Location = item.Location,
            Amount = item.Amount,
            SceneIndex = item.SceneIndex,
            Timestamp = item.Timestamp
        }));
        clone.RecentEvents.AddRange(source.RecentEvents);
        Replace(clone.RegionalNoiseLevels, source.RegionalNoiseLevels);
        Replace(clone.EventCooldowns, source.EventCooldowns);
        Replace(clone.EventOccurrenceCounts, source.EventOccurrenceCounts);
        Replace(clone.PlayerStyleScores, source.PlayerStyleScores);
        Replace(clone.CharacterMoodHints, source.CharacterMoodHints);
        clone.Flags.UnionWith(source.Flags);
        Replace(clone.Variables, source.Variables);
        return clone;
    }

    private bool IsEventEligible(DirectorEvent directorEvent, StoryScene scene, NightDirectorState state)
    {
        if (scene.DirectorRules.ForceNoMajorEvent && directorEvent.IsMajor)
        {
            return false;
        }
        if (directorEvent.Id.Equals(state.LastEventId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (state.EventCooldowns.GetValueOrDefault(directorEvent.Id) > 0)
        {
            return false;
        }
        if (directorEvent.MaxOccurrences > 0 && state.EventOccurrenceCounts.GetValueOrDefault(directorEvent.Id) >= directorEvent.MaxOccurrences)
        {
            return false;
        }
        if (directorEvent.RequiresNight && !state.IsNight)
        {
            return false;
        }
        if (directorEvent.RequiresDay && state.IsNight)
        {
            return false;
        }
        if (state.NoiseLevel < directorEvent.MinimumNoise || state.NoiseLevel > directorEvent.MaximumNoise)
        {
            return false;
        }
        if (state.TensionLevel < directorEvent.MinimumTension || state.TensionLevel > directorEvent.MaximumTension)
        {
            return false;
        }
        if (state.ThreatScore < directorEvent.MinimumThreat || state.ThreatScore > directorEvent.MaximumThreat)
        {
            return false;
        }
        if (directorEvent.RequiredFlags.Any(flag => !state.Flags.Contains(flag)))
        {
            return false;
        }
        if (directorEvent.BlockedFlags.Any(flag => state.Flags.Contains(flag)))
        {
            return false;
        }
        if (directorEvent.RequiredPreviousEvents.Any(id => !state.RecentEvents.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (directorEvent.BlockedPreviousEvents.Any(id => state.RecentEvents.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (directorEvent.RequiredLocationTags.Count > 0 && !directorEvent.RequiredLocationTags.Any(tag => state.RegionTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (directorEvent.BlockedLocationTags.Any(tag => state.RegionTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (scene.DirectorRules.AllowedEventTags.Count > 0 && !directorEvent.Tags.Any(tag => scene.DirectorRules.AllowedEventTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (directorEvent.Tags.Any(tag => scene.DirectorRules.BlockedEventTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (directorEvent.IsMajor && !CanTriggerMajorEvent())
        {
            return false;
        }
        if (directorEvent.IsMinor && !CanTriggerMinorEvent())
        {
            return false;
        }
        return true;
    }

    private void ApplyDirectorEventEffect(DirectorEventEffect effect)
    {
        switch (effect.Type.ToLowerInvariant())
        {
            case "changethreat":
                ApplyThreatDelta(effect.Amount);
                break;
            case "changetension":
                ApplyTensionDelta(effect.Amount, effect.Type);
                break;
            case "changenoise":
                AddNoise(string.IsNullOrWhiteSpace(effect.Key) ? "event" : effect.Key, effect.Amount, _state.CurrentLocation);
                break;
            case "addflag":
                _state.Flags.Add(!string.IsNullOrWhiteSpace(effect.Value) ? effect.Value : effect.Key);
                break;
            case "removeflag":
                _state.Flags.Remove(!string.IsNullOrWhiteSpace(effect.Value) ? effect.Value : effect.Key);
                break;
            case "addregionalnoise":
                AddRegionalNoise(!string.IsNullOrWhiteSpace(effect.Target) ? effect.Target : effect.Key, effect.Type, effect.Amount);
                break;
            case "startrecovery":
                StartRecovery(effect.Amount <= 0 ? 2 : effect.Amount);
                break;
            case "addmessage":
            case "addtaskhint":
            case "setlocationdangerhint":
                if (!string.IsNullOrWhiteSpace(effect.Value))
                {
                    _state.CharacterMoodHints[effect.Type] = effect.Value;
                }
                break;
            case "changeinfectedestimate":
                _state.NearbyInfectedEstimate = Clamp(_state.NearbyInfectedEstimate + effect.Amount);
                break;
            default:
                _diagnostics.Warning($"Bilinmeyen Director event effect atlandı: {effect.Type}");
                break;
        }
    }

    private void RegisterActiveNoise(string source, string location, int strength)
    {
        var existing = _state.ActiveNoiseSources.FirstOrDefault(item =>
            item.Source.Equals(source, StringComparison.OrdinalIgnoreCase) &&
            item.Location.Equals(location, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Strength = Clamp(existing.Strength + Math.Max(1, strength / 2));
            existing.RemainingScenes = Math.Max(existing.RemainingScenes, IsLongNoiseSource(source) ? 4 : 2);
            return;
        }
        _state.ActiveNoiseSources.Add(new NoiseSourceState
        {
            Source = source,
            Location = location,
            Strength = Clamp(strength),
            RemainingScenes = IsLongNoiseSource(source) ? 4 : 2
        });
    }

    private void AddNoiseHistory(string source, string location, int amount)
    {
        _state.NoiseHistory.Add(new NoiseHistoryEntry
        {
            Source = source,
            Location = location,
            Amount = amount,
            SceneIndex = _state.SceneCounter
        });
        while (_state.NoiseHistory.Count > NoiseHistoryLimit)
        {
            _state.NoiseHistory.RemoveAt(0);
        }
    }

    private static int MoodModifier(string mood)
    {
        if (mood.Contains("Kork", StringComparison.OrdinalIgnoreCase)) return -14;
        if (mood.Contains("Öfke", StringComparison.OrdinalIgnoreCase) || mood.Contains("Sinir", StringComparison.OrdinalIgnoreCase)) return -10;
        if (mood.Contains("Yorg", StringComparison.OrdinalIgnoreCase)) return -8;
        if (mood.Contains("Sakin", StringComparison.OrdinalIgnoreCase)) return 8;
        if (mood.Contains("Güven", StringComparison.OrdinalIgnoreCase)) return 10;
        return 0;
    }

    private static int PlayerStyleTrustBias(Dictionary<string, int> scores) =>
        (scores.GetValueOrDefault("Loyal") / 2) +
        (scores.GetValueOrDefault("Protective") / 3) +
        (scores.GetValueOrDefault("Leader") / 4) -
        (scores.GetValueOrDefault("Selfish") / 2) -
        scores.GetValueOrDefault("Untrustworthy");

    private static bool MatchesSpecialty(CharacterDefinition definition, CharacterHelpRequest request)
    {
        var text = $"{definition.Specialty} {definition.Profession} {definition.PlayStyle}".ToLowerInvariant();
        var type = request.RequestType.ToLowerInvariant();
        return text.Contains(type) ||
            type.Contains("lock") && text.Contains("tekn") ||
            type.Contains("heal") && (text.Contains("sağ") || text.Contains("doktor")) ||
            type.Contains("break") && (text.Contains("güç") || text.Contains("koruma")) ||
            type.Contains("scout") && text.Contains("sokak") ||
            type.Contains("calm") && text.Contains("sakin");
    }

    private static List<StoryEffect> BuildHelpEffects(string characterId, CharacterHelpResult result)
    {
        var trustDelta = result switch
        {
            CharacterHelpResult.Accept => 2,
            CharacterHelpResult.ReluctantAccept => 0,
            CharacterHelpResult.ConditionalAccept => 1,
            CharacterHelpResult.SuggestAlternative => -1,
            CharacterHelpResult.Refuse => -2,
            CharacterHelpResult.AngryRefuse => -5,
            _ => 0
        };
        return trustDelta == 0 ? [] : [new StoryEffect { Type = "ChangeRelationship", Target = characterId, Amount = trustDelta }];
    }

    private static string BuildHelpMessage(CharacterDefinition definition, CharacterState helper, CharacterHelpResult result, int trust, CharacterHelpRequest request)
    {
        var prefix = definition.Id.ToLowerInvariant() switch
        {
            "ambatukam" => result is CharacterHelpResult.Refuse or CharacterHelpResult.AngryRefuse ? "Bu iş kör dalış." : "Tamam, yolu açarım.",
            "yesking" => result is CharacterHelpResult.Refuse or CharacterHelpResult.AngryRefuse ? "Harika fikir, kendimizi kilide yem edelim. Yok." : "Kilit benim işim, ama sessiz ol.",
            "nebi" => result is CharacterHelpResult.Refuse or CharacterHelpResult.AngryRefuse ? "Bu koşulda müdahale sağlıklı değil." : "Kontrol ederim, ama panik yapmayın.",
            "bedo" => result is CharacterHelpResult.Refuse or CharacterHelpResult.AngryRefuse ? "Yok kanka, bu sefer yemem." : "Ben bakar gelirim, kısa keserim.",
            "enes" => result is CharacterHelpResult.Refuse or CharacterHelpResult.AngryRefuse ? "Şimdi bunu zorlamak herkesi dağıtır." : "Tamam, önce nefesleri düzene sokalım.",
            "büşra" or "busra" => result is CharacterHelpResult.Refuse or CharacterHelpResult.AngryRefuse ? "Yaralı varken bunu riske atmam." : "Önce güvenlik, sonra müdahale.",
            "kerim" => result is CharacterHelpResult.Refuse or CharacterHelpResult.AngryRefuse ? "Bu mekanizma elde kalır, zorlamam." : "Anahtarı ver, söker bakarım.",
            _ => result is CharacterHelpResult.Refuse or CharacterHelpResult.AngryRefuse ? "Şu an bunu yapamam." : "Denerim."
        };
        return result switch
        {
            CharacterHelpResult.Accept => prefix,
            CharacterHelpResult.ReluctantAccept => $"{prefix} Ama bunu bana borçlusun.",
            CharacterHelpResult.ConditionalAccept => $"{prefix} Bir şartla: işi uzatmayacağız.",
            CharacterHelpResult.SuggestAlternative => "Bunu ben yapmayayım; daha sessiz bir yol deneyelim.",
            CharacterHelpResult.Refuse => prefix,
            CharacterHelpResult.AngryRefuse => trust < 20 ? $"{prefix} Bana güvenmeyip şimdi yardım isteme." : prefix,
            _ => prefix
        };
    }

    private static string CharacterVoiceLine(string characterId, int trust, string mood, string topic)
    {
        var lowTrust = trust < 35;
        return characterId.ToLowerInvariant() switch
        {
            "ambatukam" => lowTrust ? "Kısa konuş. Risk varsa önce ben tartarım." : "Arkanı kolluyorum, ama saçmalama.",
            "yesking" => lowTrust ? "Plan buysa kilit değil, bizi açarlar." : "Teknik olarak berbat, pratikte iş görür.",
            "nebi" => lowTrust ? "Veri eksik. Böyle karar alınmaz." : "Belirti kötü ama kontrol altında tutabiliriz.",
            "bedo" => lowTrust ? "Bana patlamasın da ne yaparsan yap." : "Tamam kanka, hızlı girip çıkalım.",
            "enes" => lowTrust ? "Herkes gerildi. Bir adım geri çekil." : "Sakin kalırsak buradan çıkarız.",
            "büşra" or "busra" => lowTrust ? "Yaralıları riske atacak karar istemiyorum." : "Ben yaralıları tutarım, sen yolu temizle.",
            "kerim" => lowTrust ? "Zorlarsan parça elimizde kalır." : "Motor gibi düşün, doğru yerden basarsan açılır.",
            _ => lowTrust ? "Emin değilim." : "Yanındayım."
        };
    }

    private static string TrustLabel(int trust) => trust switch
    {
        >= 75 => "Yüksek",
        >= 45 => "Orta",
        >= 20 => "Düşük",
        _ => "Öfkeli"
    };

    private void ApplyThreatDelta(int amount)
    {
        if (amount == 0)
        {
            return;
        }
        _state.Variables["threatAdjustment"] = ClampSigned(_state.Variables.GetValueOrDefault("threatAdjustment") + amount);
    }

    private void DecayCooldowns()
    {
        foreach (var key in _state.EventCooldowns.Keys.ToList())
        {
            _state.EventCooldowns[key]--;
            if (_state.EventCooldowns[key] <= 0)
            {
                _state.EventCooldowns.Remove(key);
            }
        }
        _state.MajorEventCooldown = Math.Max(0, _state.MajorEventCooldown - 1);
        _state.MinorEventCooldown = Math.Max(0, _state.MinorEventCooldown - 1);
    }

    private void DecayEventPressure()
    {
        _state.RecoveryScenesRemaining = Math.Max(0, _state.RecoveryScenesRemaining - 1);
        if (_state.RecoveryScenesRemaining == 0 && _state.CurrentPacingState == PacingState.Recovery)
        {
            UpdatePacingState();
        }
    }

    private int CalculateLocationPressure()
    {
        var pressure = _state.Variables.GetValueOrDefault("sceneThreatHint") + _state.Variables.GetValueOrDefault("lowLight");
        foreach (var tag in _state.RegionTags)
        {
            pressure += tag.ToLowerInvariant() switch
            {
                "indoor" => 2,
                "outdoor" => 1,
                "urban" => 4,
                "unknown" => 5,
                "hospital" => 5,
                "tunnel" => 6,
                "safehouse" => -8,
                _ => 0
            };
        }
        return pressure;
    }

    private void UpdatePacingState()
    {
        _state.CurrentPacingState = _state.RecoveryScenesRemaining > 0
            ? PacingState.Recovery
            : _state.TensionLevel switch
            {
                < 20 => PacingState.Calm,
                < 40 => PacingState.Uneasy,
                < 65 => PacingState.Rising,
                _ => PacingState.High
            };
    }

    private bool IsCurrentLocation(string location) =>
        string.IsNullOrWhiteSpace(_state.CurrentLocation) ||
        location.Equals(_state.CurrentLocation, StringComparison.OrdinalIgnoreCase);

    private static bool IsLongNoiseSource(string source) =>
        source.Equals("alarm", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("vehicle_engine", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("generator", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("speaker", StringComparison.OrdinalIgnoreCase);

    private static bool IsNightTime(string time)
    {
        if (string.IsNullOrWhiteSpace(time))
        {
            return false;
        }
        var normalized = time.Trim().ToLowerInvariant();
        if (normalized is "night" or "gece")
        {
            return true;
        }
        if (normalized is "day" or "evening" or "dawn" or "unknown" or "gündüz" or "sabah" or "akşam")
        {
            return false;
        }
        if (!TimeSpan.TryParse(time, out var parsed))
        {
            return false;
        }
        return parsed.Hours >= 20 || parsed.Hours < 6;
    }

    private static void Replace<TKey, TValue>(Dictionary<TKey, TValue> target, IEnumerable<KeyValuePair<TKey, TValue>> source)
        where TKey : notnull
    {
        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static int StableHash(params string[] parts)
    {
        unchecked
        {
            var hash = 17;
            foreach (var part in parts)
            {
                foreach (var character in part)
                {
                    hash = (hash * 31) + character;
                }
            }
            return Math.Abs(hash);
        }
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);

    private static int ClampSigned(int value) => Math.Clamp(value, -100, 100);
}

public static class NightDirector
{
    public static void Initialize(GameState state)
    {
        var service = CreateService(state);
        service.Initialize(state.Director.DirectorSeed, state.Director.CurrentSeasonId, state.CurrentChapterId);
        state.Director = service.SaveStateSnapshot();
    }

    public static DirectorEventResult? AdvanceScene(GameState state, StoryScene scene)
    {
        var service = CreateService(state);
        service.GetState().NoiseLevel = state.NoiseLevel;
        service.GetState().CurrentLocation = state.Location;
        service.OnSceneEntered(scene);
        state.Director = service.SaveStateSnapshot();
        state.NoiseLevel = state.Director.NoiseLevel;
        state.ThreatLevel = state.Director.ThreatScore;
        return null;
    }

    public static void RecordChoice(GameState state, StoryChoice choice)
    {
        var service = CreateService(state);
        service.GetState().CurrentLocation = state.Location;
        service.ApplyChoiceNoiseAndThreat(choice);
        state.Director = service.SaveStateSnapshot();
        state.NoiseLevel = state.Director.NoiseLevel;
        state.ThreatLevel = state.Director.ThreatScore;
    }

    public static bool ShouldHelperAccept(GameState state, RelationshipState? relationship, CharacterState helper, int baseChance)
    {
        var service = CreateService(state);
        var trust = relationship?.Trust ?? 50;
        var injuryPenalty = helper.Injury == "Yok" ? 0 : 18;
        var chance = Math.Clamp(baseChance + trust / 3 - state.Fatigue / 5 - injuryPenalty - service.GetState().TensionLevel / 8, 5, 95);
        return service.CreateDeterministicRandom(helper.Id).Next(0, 100) < chance;
    }

    private static NightDirectorService CreateService(GameState state)
    {
        var service = new NightDirectorService();
        if (state.Director.DirectorSeed == 0)
        {
            state.Director.DirectorSeed = StableHash(state.SelectedCharacterId, state.CurrentSceneId, state.Day.ToString());
        }
        service.LoadState(state.Director);
        return service;
    }

    private static int StableHash(params string[] parts)
    {
        unchecked
        {
            var hash = 17;
            foreach (var part in parts)
            {
                foreach (var character in part)
                {
                    hash = (hash * 31) + character;
                }
            }
            return Math.Abs(hash);
        }
    }
}
