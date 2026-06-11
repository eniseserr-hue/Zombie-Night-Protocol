namespace ZombieNightProtocol.Core;

public static class GameStateFactory
{
    public static GameState Create(CharacterDefinition selected, IReadOnlyList<CharacterDefinition> allCharacters, string startSceneId)
    {
        var state = new GameState
        {
            SelectedCharacterId = selected.Id,
            CurrentSceneId = startSceneId
        };

        foreach (var character in allCharacters)
        {
            state.Characters.Add(new CharacterState
            {
                Id = character.Id,
                Name = character.Name,
                IsPlayer = character.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase),
                Mood = character.Id == selected.Id ? "Kararlı" : "Temkinli"
            });

            if (character.Id != selected.Id)
            {
                state.Relationships.Add(new RelationshipState
                {
                    CharacterId = character.Id,
                    Trust = InitialTrust(character.Id)
                });
            }
        }

        foreach (var item in selected.StartingItems)
        {
            state.Inventory.Add(CloneItem(item));
        }

        state.Tasks.AddRange(
        [
            new TaskState
            {
                Id = "escape_istanbul",
                Title = "İstanbul'dan güvenli bir çıkış bul",
                Description = "Ekibi şehrin dışına çıkaracak sessiz ve sürdürülebilir bir rota belirle.",
                Hint = "Telsiz yayınları tahliye koridoru hakkında bilgi taşıyor olabilir.",
                Priority = "Ana görev",
                Progress = 12
            },
            new TaskState
            {
                Id = "restore_radio",
                Title = "Askerî telsizi çalıştır",
                Description = "Yönetici dairesindeki telsize güç ver ve frekansı yakala.",
                Hint = "Yeni pil veya yüksek Teknik değeri bu işi kolaylaştırır.",
                Priority = "Yüksek",
                Progress = 25
            },
            new TaskState
            {
                Id = "secure_supplies",
                Title = "İlk gece için erzak topla",
                Description = "Su, yiyecek ve temel sağlık malzemelerini güvenceye al.",
                Hint = "Daireleri aramak zaman ve gürültü riski yaratır.",
                Priority = "Normal",
                Progress = 20
            },
            new TaskState
            {
                Id = "keep_group_calm",
                Title = "Ekibin moralini koru",
                Description = "Güven kaybını ve grubun dağılmasını önle.",
                Hint = "Kararların yalnızca rotayı değil ilişkileri de değiştirir.",
                Priority = "Sürekli",
                Progress = 70
            }
        ]);
        state.Journal.Add(new JournalEntry
        {
            Text = "Gece çöktü. Esenler'deki apartmandan çıkmak zorundasın.",
            Category = "Başlangıç",
            Location = state.Location,
            Day = state.Day,
            GameTime = state.Time.ToString(@"hh\:mm")
        });
        return state;
    }

    private static int InitialTrust(string characterId) => characterId switch
    {
        "nebi" => 68,
        "enes" => 64,
        "bedo" => 48,
        "busra" => 45,
        "yesking" => 42,
        "kerim" => 58,
        _ => 50
    };

    internal static ItemState CloneItem(ItemState item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Description = item.Description,
        Category = item.Category,
        Quantity = item.Quantity,
        Weight = item.Weight,
        IsUsable = item.IsUsable,
        IsQuestItem = item.IsQuestItem,
        IsConsumable = item.IsConsumable,
        Damage = item.Damage,
        Healing = item.Healing,
        Durability = item.Durability,
        StoryTag = item.StoryTag
    };
}

public sealed class ConditionEvaluator(IGameDiagnostics? diagnostics = null)
{
    private readonly IGameDiagnostics _diagnostics = diagnostics ?? new NullGameDiagnostics();

    public bool Evaluate(StoryCondition condition, GameState state, IReadOnlyDictionary<string, CharacterDefinition> characters)
    {
        try
        {
            return condition.Type.ToLowerInvariant() switch
            {
                "statcondition" or "skillcondition" => CompareNumber(GetStat(condition, state, characters), condition.Operator, condition.Amount),
                "inventorycondition" => CompareBoolean(state.Inventory.Any(i => Match(i.Id, condition.Value) || Match(i.Name, condition.Value)), condition.Operator),
                "itemquantitycondition" => CompareNumber(state.Inventory.FirstOrDefault(i => Match(i.Id, condition.Key))?.Quantity ?? 0, condition.Operator, condition.Amount),
                "relationshipcondition" => CompareNumber(state.Relationships.FirstOrDefault(r => Match(r.CharacterId, condition.Target))?.Trust ?? 0, condition.Operator, condition.Amount),
                "characteralivecondition" => CompareBoolean(state.Characters.FirstOrDefault(c => Match(c.Id, condition.Target))?.IsAlive == true, condition.Operator),
                "characterdeadcondition" => CompareBoolean(state.Characters.FirstOrDefault(c => Match(c.Id, condition.Target))?.IsAlive == false, condition.Operator),
                "flagcondition" => CompareBoolean(state.Flags.Contains(condition.Value), condition.Operator),
                "countercondition" => CompareNumber(state.Counters.GetValueOrDefault(condition.Key), condition.Operator, condition.Amount),
                "locationcondition" => CompareString(state.Location, condition.Operator, condition.Value),
                "timecondition" => CompareNumber((int)state.Time.TotalMinutes, condition.Operator, condition.Amount),
                "isnightcondition" => CompareBoolean(state.IsNight, condition.Operator, condition.Value),
                "healthcondition" => CompareNumber(state.Health, condition.Operator, condition.Amount),
                "infectioncondition" => CompareNumber(state.Infection, condition.Operator, condition.Amount),
                "choicehistorycondition" => CompareBoolean(state.ChoiceHistory.Contains(condition.Value), condition.Operator),
                "selectedcharactercondition" => CompareString(state.SelectedCharacterId, condition.Operator, condition.Value),
                _ => Unknown(condition.Type)
            };
        }
        catch (Exception exception)
        {
            _diagnostics.Warning($"Koşul değerlendirilemedi ({condition.Type}): {exception.Message}");
            return false;
        }
    }

    private bool Unknown(string type)
    {
        _diagnostics.Warning($"Bilinmeyen condition türü: {type}");
        return false;
    }

    private static int GetStat(StoryCondition condition, GameState state, IReadOnlyDictionary<string, CharacterDefinition> characters)
    {
        if (!characters.TryGetValue(state.SelectedCharacterId, out var character))
        {
            return 0;
        }

        return character.Stats.GetValueOrDefault(condition.Key);
    }

    private static bool Match(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool CompareBoolean(bool actual, string op, string expected = "true")
    {
        var wanted = !bool.TryParse(expected, out var parsed) || parsed;
        return op switch
        {
            "==" => actual == wanted,
            "!=" => actual != wanted,
            _ => actual == wanted
        };
    }

    private static bool CompareNumber(int actual, string op, int expected) => op.Replace(" ", "", StringComparison.Ordinal) switch
    {
        "==" => actual == expected,
        "!=" => actual != expected,
        ">" => actual > expected,
        ">=" => actual >= expected,
        "<" => actual < expected,
        "<=" => actual <= expected,
        _ => false
    };

    private static bool CompareString(string actual, string op, string expected) => op switch
    {
        "==" => Match(actual, expected),
        "!=" => !Match(actual, expected),
        "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        "notContains" => !actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}

public sealed class EffectProcessor(IGameDiagnostics? diagnostics = null)
{
    private readonly IGameDiagnostics _diagnostics = diagnostics ?? new NullGameDiagnostics();

    public void Apply(StoryEffect effect, GameState state)
    {
        try
        {
            switch (effect.Type.ToLowerInvariant())
            {
                case "additem": AddItem(effect, state); break;
                case "removeitem": RemoveItem(effect, state); break;
                case "changeitemquantity": ChangeItemQuantity(effect, state); break;
                case "changeitemdurability": ChangeItemDurability(effect, state); break;
                case "changehealth": state.Health = Clamp(state.Health + effect.Amount); break;
                case "changehunger": state.Hunger = Clamp(state.Hunger + effect.Amount); break;
                case "changethirst": state.Thirst = Clamp(state.Thirst + effect.Amount); break;
                case "changesanity": state.Sanity = Clamp(state.Sanity + effect.Amount); break;
                case "changemorale": state.Morale = Clamp(state.Morale + effect.Amount); break;
                case "changeinfection": state.Infection = Clamp(state.Infection + effect.Amount); break;
                case "changerelationship": ChangeRelationship(effect, state); break;
                case "changecharactermood": FindCharacter(effect, state).Mood = effect.Value; break;
                case "changecharacterthought": FindCharacter(effect, state).LastThought = effect.Value; break;
                case "injurecharacter": FindCharacter(effect, state).Injury = effect.Value; break;
                case "healcharacter": HealCharacter(effect, state); break;
                case "killcharacter": KillCharacter(effect, state); break;
                case "revivecharacter": ReviveCharacter(effect, state); break;
                case "setflag": state.Flags.Add(effect.Value); break;
                case "removeflag": state.Flags.Remove(effect.Value); break;
                case "incrementcounter": state.Counters[effect.Key] = state.Counters.GetValueOrDefault(effect.Key) + effect.Amount; break;
                case "setcounter": state.Counters[effect.Key] = effect.Amount; break;
                case "unlockroute": state.UnlockedRoutes.Add(effect.Value); break;
                case "unlockcharacterinfo": FindCharacter(effect, state).HiddenInfoUnlocked = true; break;
                case "usespecialability": FindCharacter(effect, state).SpecialAbilityUsed = true; break;
                case "setcheckpoint": state.CheckpointSceneId = string.IsNullOrWhiteSpace(effect.Value) ? state.CurrentSceneId : effect.Value; break;
                case "addjournalentry": AddJournalEntry(effect, state); break;
                case "addtask": state.Tasks.Add(new TaskState { Id = effect.Key, Title = effect.Value }); break;
                case "completetask": CompleteTask(effect, state); break;
                case "changenoise": state.NoiseLevel = Clamp(state.NoiseLevel + effect.Amount); break;
                case "changethreat": state.ThreatLevel = Clamp(state.ThreatLevel + effect.Amount); break;
                case "advancetime": AdvanceTime(effect.Amount, state); break;
                case "triggerending": TriggerEnding(effect, state); break;
                default: _diagnostics.Warning($"Bilinmeyen effect türü: {effect.Type}"); break;
            }
        }
        catch (Exception exception)
        {
            _diagnostics.Warning($"Effect uygulanamadı ({effect.Type}): {exception.Message}");
        }
    }

    private static void AddItem(StoryEffect effect, GameState state)
    {
        var item = effect.Item ?? new ItemState { Id = effect.Key, Name = effect.Value, Quantity = Math.Max(1, effect.Amount) };
        var existing = state.Inventory.FirstOrDefault(i => i.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            state.Inventory.Add(GameStateFactory.CloneItem(item));
        }
        else
        {
            existing.Quantity += Math.Max(1, item.Quantity);
        }
    }

    private static void RemoveItem(StoryEffect effect, GameState state)
    {
        var item = state.Inventory.FirstOrDefault(i => i.Id.Equals(effect.Key, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            state.Inventory.Remove(item);
        }
    }

    private static void ChangeItemQuantity(StoryEffect effect, GameState state)
    {
        var item = state.Inventory.FirstOrDefault(i => i.Id.Equals(effect.Key, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        item.Quantity += effect.Amount;
        if (item.Quantity <= 0)
        {
            state.Inventory.Remove(item);
        }
    }

    private static void ChangeItemDurability(StoryEffect effect, GameState state)
    {
        var item = state.Inventory.FirstOrDefault(i => i.Id.Equals(effect.Key, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }
        item.Durability = Math.Clamp(item.Durability + effect.Amount, 0, 100);
        if (item.Durability == 0)
        {
            state.Inventory.Remove(item);
        }
    }

    private static void ChangeRelationship(StoryEffect effect, GameState state)
    {
        var relationship = state.Relationships.FirstOrDefault(r => r.CharacterId.Equals(effect.Target, StringComparison.OrdinalIgnoreCase));
        if (relationship is not null)
        {
            relationship.Trust = Clamp(relationship.Trust + effect.Amount);
        }
    }

    private static CharacterState FindCharacter(StoryEffect effect, GameState state) =>
        state.Characters.First(c => c.Id.Equals(effect.Target, StringComparison.OrdinalIgnoreCase));

    private static void HealCharacter(StoryEffect effect, GameState state)
    {
        var character = FindCharacter(effect, state);
        character.Health = Clamp(character.Health + Math.Max(1, effect.Amount));
        character.Injury = "Yok";
    }

    private static void KillCharacter(StoryEffect effect, GameState state)
    {
        var character = FindCharacter(effect, state);
        character.IsAlive = false;
        character.Health = 0;
        character.Mood = "Öldü";
        if (character.IsPlayer)
        {
            state.Ending = EndingKind.Death;
            state.EndingReason = string.IsNullOrWhiteSpace(effect.Value) ? "Enfekte saldırısı" : effect.Value;
        }
    }

    private static void ReviveCharacter(StoryEffect effect, GameState state)
    {
        var character = FindCharacter(effect, state);
        character.IsAlive = true;
        character.Health = Math.Max(1, effect.Amount);
        character.Mood = "Bitkin";
    }

    private static void CompleteTask(StoryEffect effect, GameState state)
    {
        var task = state.Tasks.FirstOrDefault(t => t.Id.Equals(effect.Key, StringComparison.OrdinalIgnoreCase));
        if (task is not null)
        {
            task.IsCompleted = true;
            task.Progress = 100;
        }
    }

    private static void AddJournalEntry(StoryEffect effect, GameState state) =>
        state.Journal.Add(new JournalEntry
        {
            Text = effect.Value,
            Category = string.IsNullOrWhiteSpace(effect.Key) ? "Karar" : effect.Key,
            Location = state.Location,
            Day = state.Day,
            GameTime = state.Time.ToString(@"hh\:mm")
        });

    private static void AdvanceTime(int minutes, GameState state)
    {
        GameClock.AdvanceGameTime(state, TimeSpan.FromMinutes(minutes));
    }

    private static void TriggerEnding(StoryEffect effect, GameState state)
    {
        state.Ending = effect.Key.ToLowerInvariant() switch
        {
            "death" => EndingKind.Death,
            "gamecomplete" => EndingKind.GameComplete,
            _ => EndingKind.ChapterComplete
        };
        state.EndingReason = effect.Value;
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}

public sealed class StoryEngine
{
    private readonly StoryPackage _story;
    private readonly IReadOnlyDictionary<string, StoryScene> _scenes;
    private readonly IReadOnlyDictionary<string, CharacterDefinition> _characters;
    private readonly ConditionEvaluator _conditions;
    private readonly EffectProcessor _effects;

    public StoryEngine(
        StoryPackage story,
        IReadOnlyList<CharacterDefinition> characters,
        IGameDiagnostics? diagnostics = null)
    {
        _story = story;
        _scenes = story.Scenes.ToDictionary(scene => scene.Id, StringComparer.OrdinalIgnoreCase);
        _characters = characters.ToDictionary(character => character.Id, StringComparer.OrdinalIgnoreCase);
        _conditions = new ConditionEvaluator(diagnostics);
        _effects = new EffectProcessor(diagnostics);
    }

    public StoryScene CurrentScene(GameState state) =>
        _scenes.TryGetValue(state.CurrentSceneId, out var scene)
            ? scene
            : throw new InvalidOperationException($"Sahne bulunamadı: {state.CurrentSceneId}");

    public IReadOnlyList<ChoiceAvailability> GetChoices(GameState state)
    {
        var scene = CurrentScene(state);
        return scene.Choices
            .Select(choice =>
            {
                var available = choice.Conditions.All(condition => _conditions.Evaluate(condition, state, _characters));
                return new ChoiceAvailability(choice, available, available ? "" : choice.LockedReason);
            })
            .ToList();
    }

    public StoryScene Choose(GameState state, string choiceId)
    {
        var scene = CurrentScene(state);
        var available = GetChoices(state).FirstOrDefault(item => item.Choice.Id.Equals(choiceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Seçenek bulunamadı: {choiceId}");
        if (!available.IsAvailable)
        {
            throw new InvalidOperationException(available.LockedReason);
        }

        state.LastChoiceText = available.Choice.Text;
        return ApplyChoice(state, available.Choice);
    }

    public StoryScene ChooseWithAssistance(GameState state, string choiceId, string helperId)
    {
        var scene = CurrentScene(state);
        var choice = scene.Choices.FirstOrDefault(item => item.Id.Equals(choiceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Seçenek bulunamadı: {choiceId}");
        state.LastChoiceText = $"{choice.Text} ({helperId} yardım etti)";
        return ApplyChoice(state, choice);
    }

    private StoryScene ApplyChoice(GameState state, StoryChoice choice)
    {
        if (!choice.IsCriticalTimed)
        {
            state.UndoSnapshot = GameStateCloner.Clone(state, includeTransient: false);
            state.CanUndo = true;
        }
        else
        {
            state.UndoSnapshot = null;
            state.CanUndo = false;
        }

        state.ChoiceCount++;
        state.ChoiceHistory.Add(choice.Id);
        NightDirector.RecordChoice(state, choice);
        foreach (var effect in choice.Effects)
        {
            _effects.Apply(effect, state);
        }

        if (!string.IsNullOrWhiteSpace(choice.NextSceneId))
        {
            state.CurrentSceneId = choice.NextSceneId;
        }

        var next = CurrentScene(state);
        ApplySceneMetadata(next, state);
        NightDirector.AdvanceScene(state, next);
        if (next.IsCheckpoint)
        {
            state.CheckpointSceneId = next.Id;
            state.CheckpointSnapshot = GameStateCloner.Clone(state, includeTransient: false);
        }

        return next;
    }

    public bool Undo(GameState state)
    {
        if (!state.CanUndo || state.UndoSnapshot is null)
        {
            return false;
        }

        GameStateCloner.CopyInto(state.UndoSnapshot, state);
        state.CanUndo = false;
        state.UndoSnapshot = null;
        return true;
    }

    public bool RestoreCheckpoint(GameState state)
    {
        if (state.CheckpointSnapshot is null)
        {
            return false;
        }

        GameStateCloner.CopyInto(state.CheckpointSnapshot, state);
        state.Ending = EndingKind.None;
        state.EndingReason = "";
        return true;
    }

    public string StartSceneId => _story.StartSceneId;

    private static void ApplySceneMetadata(StoryScene scene, GameState state)
    {
        state.CurrentChapterId = scene.ChapterId;
        state.City = scene.City;
        state.Location = scene.Location;
        if (TimeSpan.TryParse(scene.Time, out var parsedTime))
        {
            var currentAbsolute = TimeSpan.FromDays(state.Day - 1) + state.Time;
            var sceneAbsolute = TimeSpan.FromDays(scene.Day - 1) + parsedTime;
            if (sceneAbsolute > currentAbsolute)
            {
                state.Day = scene.Day;
                state.Time = parsedTime;
            }
        }
        state.IsNight = state.Time.Hours >= 20 || state.Time.Hours < 6;
    }
}

public static class GameClock
{
    public static readonly TimeSpan GameTimePerRealSecond = TimeSpan.FromSeconds(144);

    public static void AdvanceRealSecond(GameState state) =>
        AdvanceGameTime(state, GameTimePerRealSecond);

    public static void AdvanceGameTime(GameState state, TimeSpan amount)
    {
        var total = state.Time.Add(amount);
        while (total >= TimeSpan.FromDays(1))
        {
            total -= TimeSpan.FromDays(1);
            state.Day++;
        }

        state.Time = total;
        state.IsNight = total.Hours >= 20 || total.Hours < 6;
    }
}

public static class GameStateCloner
{
    public static GameState Clone(GameState source, bool includeTransient)
    {
        var clone = new GameState();
        CopyInto(source, clone);
        if (includeTransient)
        {
            clone.UndoSnapshot = source.UndoSnapshot is null ? null : Clone(source.UndoSnapshot, false);
            clone.CheckpointSnapshot = source.CheckpointSnapshot is null ? null : Clone(source.CheckpointSnapshot, false);
        }
        return clone;
    }

    public static void CopyInto(GameState source, GameState target)
    {
        target.SelectedCharacterId = source.SelectedCharacterId;
        target.CurrentSceneId = source.CurrentSceneId;
        target.CurrentChapterId = source.CurrentChapterId;
        target.City = source.City;
        target.Location = source.Location;
        target.Day = source.Day;
        target.Time = source.Time;
        target.IsNight = source.IsNight;
        target.Health = source.Health;
        target.Hunger = source.Hunger;
        target.Thirst = source.Thirst;
        target.Sanity = source.Sanity;
        target.Morale = source.Morale;
        target.Fatigue = source.Fatigue;
        target.Infection = source.Infection;
        target.NoiseLevel = source.NoiseLevel;
        target.ThreatLevel = source.ThreatLevel;
        target.InfectionRisk = source.InfectionRisk;
        target.PlayedSeconds = source.PlayedSeconds;
        target.LastSavedAt = source.LastSavedAt;
        target.Ending = source.Ending;
        target.EndingReason = source.EndingReason;
        target.LastChoiceText = source.LastChoiceText;
        target.ChoiceCount = source.ChoiceCount;
        target.CanUndo = source.CanUndo;
        target.SessionInProgress = source.SessionInProgress;
        target.CheckpointSceneId = source.CheckpointSceneId;

        target.Inventory.Clear();
        target.Inventory.AddRange(source.Inventory.Select(GameStateFactory.CloneItem));
        target.Characters.Clear();
        target.Characters.AddRange(source.Characters.Select(c => new CharacterState
        {
            Id = c.Id,
            Name = c.Name,
            IsPlayer = c.IsPlayer,
            IsAlive = c.IsAlive,
            Health = c.Health,
            Infection = c.Infection,
            Mood = c.Mood,
            LastThought = c.LastThought,
            Injury = c.Injury,
            HiddenInfoUnlocked = c.HiddenInfoUnlocked,
            SpecialAbilityUsed = c.SpecialAbilityUsed
        }));
        target.Relationships.Clear();
        target.Relationships.AddRange(source.Relationships.Select(r => new RelationshipState { CharacterId = r.CharacterId, Trust = r.Trust }));
        Replace(target.Flags, source.Flags);
        Replace(target.Counters, source.Counters);
        Replace(target.ChoiceHistory, source.ChoiceHistory);
        Replace(target.UnlockedRoutes, source.UnlockedRoutes);
        Replace(target.VisitedScenes, source.VisitedScenes);
        Replace(target.ListenedVoiceMessages, source.ListenedVoiceMessages);
        Replace(target.PersistentChoices, source.PersistentChoices.Select(pair => new KeyValuePair<string, PersistentChoiceState>(pair.Key, ClonePersistentChoice(pair.Value))));
        target.CharacterMemories.Clear();
        target.CharacterMemories.AddRange(source.CharacterMemories.Select(CloneCharacterMemory));
        Replace(target.Promises, source.Promises);
        Replace(target.EnemyGroups, source.EnemyGroups);
        Replace(target.AlliedGroups, source.AlliedGroups);
        Replace(target.RouteSelections, source.RouteSelections);
        target.Vehicle = CloneVehicle(source.Vehicle);
        target.Director = CloneDirector(source.Director);
        target.Journal.Clear();
        target.Journal.AddRange(source.Journal.Select(j => new JournalEntry
        {
            Timestamp = j.Timestamp,
            Text = j.Text,
            Category = j.Category,
            Location = j.Location,
            Day = j.Day,
            GameTime = j.GameTime
        }));
        target.Tasks.Clear();
        target.Tasks.AddRange(source.Tasks.Select(t => new TaskState
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            Hint = t.Hint,
            Priority = t.Priority,
            Progress = t.Progress,
            IsCompleted = t.IsCompleted
        }));
        target.Messages.Clear();
        target.Messages.AddRange(source.Messages.Select(message => new CharacterMessageState
        {
            Id = message.Id,
            CharacterId = message.CharacterId,
            CharacterName = message.CharacterName,
            Text = message.Text,
            Tone = message.Tone,
            TrustLevel = message.TrustLevel,
            Mood = message.Mood,
            RelationshipContext = message.RelationshipContext,
            Day = message.Day,
            GameTime = message.GameTime,
            RelatedVoiceMessageId = message.RelatedVoiceMessageId,
            Tags = [.. message.Tags],
            IsRead = message.IsRead,
            SceneId = message.SceneId
        }));
    }

    private static PersistentChoiceState ClonePersistentChoice(PersistentChoiceState source) => new()
    {
        Id = source.Id,
        Label = source.Label,
        Value = source.Value,
        RememberedByCharacters = [.. source.RememberedByCharacters],
        AffectsFutureChapters = source.AffectsFutureChapters,
        AffectsFutureSeasons = source.AffectsFutureSeasons
    };

    private static CharacterMemoryState CloneCharacterMemory(CharacterMemoryState source) => new()
    {
        CharacterId = source.CharacterId,
        MemoryId = source.MemoryId,
        SourceChoiceId = source.SourceChoiceId,
        Tone = source.Tone,
        TrustImpact = source.TrustImpact,
        FutureMessageTags = [.. source.FutureMessageTags]
    };

    private static VehicleState CloneVehicle(VehicleState source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Fuel = source.Fuel,
        Condition = source.Condition,
        Tags = [.. source.Tags]
    };

    private static NightDirectorState CloneDirector(NightDirectorState source)
        => NightDirectorService.CloneState(source);

    private static void Replace<T>(HashSet<T> target, IEnumerable<T> source)
    {
        target.Clear();
        target.UnionWith(source);
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
}
