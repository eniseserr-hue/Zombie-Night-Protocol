using ZombieNightProtocol.Core;

namespace ZombieNightProtocol.Core.Tests;

public sealed class GameEngineTests
{
    [Fact]
    public void StoryValidatorReportsDuplicateSceneId()
    {
        var story = new StoryPackage
        {
            StartSceneId = "a",
            Scenes = [Scene("a"), Scene("a")]
        };

        var issues = new StoryValidator().Validate(story);

        Assert.Contains(issues, issue => issue.Code == "duplicate_scene" && issue.Severity == "Error");
    }

    [Fact]
    public void StoryValidatorReportsMissingNextScene()
    {
        var story = new StoryPackage
        {
            StartSceneId = "a",
            Scenes =
            [
                Scene("a", new StoryChoice { Id = "go", Text = "Git", NextSceneId = "missing" })
            ]
        };

        var issues = new StoryValidator().Validate(story);

        Assert.Contains(issues, issue => issue.Code == "missing_next");
    }

    [Fact]
    public void StoryValidatorReportsMissingStart()
    {
        var story = new StoryPackage { StartSceneId = "missing", Scenes = [Scene("a")] };

        var issues = new StoryValidator().Validate(story);

        Assert.Contains(issues, issue => issue.Code == "missing_start");
    }

    [Theory]
    [InlineData("StatCondition", "Güç", 70)]
    [InlineData("SkillCondition", "Güç", 70)]
    public void StatConditionsUseSelectedCharacter(string type, string key, int minimum)
    {
        var state = State();
        var characters = new Dictionary<string, CharacterDefinition>
        {
            ["hero"] = Character("hero", new Dictionary<string, int> { ["Güç"] = 80 })
        };
        var condition = new StoryCondition { Type = type, Key = key, Operator = ">=", Amount = minimum };

        Assert.True(new ConditionEvaluator().Evaluate(condition, state, characters));
    }

    [Fact]
    public void InventoryAndQuantityConditionsWork()
    {
        var state = State();
        state.Inventory.Add(new ItemState { Id = "bandage", Name = "Bandaj", Quantity = 2 });
        var evaluator = new ConditionEvaluator();

        Assert.True(evaluator.Evaluate(
            new StoryCondition { Type = "InventoryCondition", Operator = "==", Value = "bandage" },
            state,
            Characters()));
        Assert.True(evaluator.Evaluate(
            new StoryCondition { Type = "ItemQuantityCondition", Key = "bandage", Operator = ">=", Amount = 2 },
            state,
            Characters()));
    }

    [Fact]
    public void RelationshipCharacterAliveAndFlagConditionsWork()
    {
        var state = State();
        state.Relationships.Add(new RelationshipState { CharacterId = "bedo", Trust = 65 });
        state.Characters.Add(new CharacterState { Id = "bedo", Name = "Bedo" });
        state.Flags.Add("radio_on");
        var evaluator = new ConditionEvaluator();

        Assert.True(evaluator.Evaluate(new StoryCondition { Type = "RelationshipCondition", Target = "bedo", Operator = ">=", Amount = 60 }, state, Characters()));
        Assert.True(evaluator.Evaluate(new StoryCondition { Type = "CharacterAliveCondition", Target = "bedo", Operator = "==" }, state, Characters()));
        Assert.True(evaluator.Evaluate(new StoryCondition { Type = "FlagCondition", Operator = "==", Value = "radio_on" }, state, Characters()));
    }

    [Fact]
    public void AddRemoveItemAndRelationshipEffectsWork()
    {
        var state = State();
        state.Relationships.Add(new RelationshipState { CharacterId = "bedo", Trust = 45 });
        var processor = new EffectProcessor();

        processor.Apply(new StoryEffect { Type = "AddItem", Item = new ItemState { Id = "water", Name = "Su", Quantity = 2 } }, state);
        processor.Apply(new StoryEffect { Type = "ChangeItemQuantity", Key = "water", Amount = -1 }, state);
        processor.Apply(new StoryEffect { Type = "ChangeRelationship", Target = "bedo", Amount = 20 }, state);

        Assert.Equal(1, Assert.Single(state.Inventory).Quantity);
        Assert.Equal(65, Assert.Single(state.Relationships).Trust);

        processor.Apply(new StoryEffect { Type = "RemoveItem", Key = "water" }, state);
        Assert.Empty(state.Inventory);
    }

    [Fact]
    public void CharacterDeathIsRecordedAndPlayerDeathTriggersEnding()
    {
        var state = State();
        state.Characters.Add(new CharacterState { Id = "hero", Name = "Hero", IsPlayer = true });

        new EffectProcessor().Apply(new StoryEffect { Type = "KillCharacter", Target = "hero", Value = "Isırık" }, state);

        Assert.False(state.Characters[0].IsAlive);
        Assert.Equal(EndingKind.Death, state.Ending);
        Assert.Equal("Isırık", state.EndingReason);
    }

    [Fact]
    public void NormalChoiceCanBeUndone()
    {
        var story = new StoryPackage
        {
            StartSceneId = "a",
            Scenes =
            [
                Scene("a", new StoryChoice
                {
                    Id = "go",
                    Text = "Git",
                    NextSceneId = "b",
                    Effects = [new StoryEffect { Type = "ChangeHealth", Amount = -20 }]
                }),
                Scene("b")
            ]
        };
        var engine = new StoryEngine(story, [Character("hero")]);
        var state = State();

        engine.Choose(state, "go");
        Assert.Equal(80, state.Health);
        Assert.Equal("b", state.CurrentSceneId);

        Assert.True(engine.Undo(state));
        Assert.Equal(100, state.Health);
        Assert.Equal("a", state.CurrentSceneId);
    }

    [Fact]
    public void TimedCriticalChoiceCannotBeUndone()
    {
        var story = new StoryPackage
        {
            StartSceneId = "a",
            Scenes =
            [
                Scene("a", new StoryChoice { Id = "go", Text = "Git", NextSceneId = "b", IsCriticalTimed = true }),
                Scene("b")
            ]
        };
        var engine = new StoryEngine(story, [Character("hero")]);
        var state = State();

        engine.Choose(state, "go");

        Assert.False(engine.Undo(state));
    }

    [Fact]
    public void AssistanceCanResolveLockedSkillChoice()
    {
        var story = new StoryPackage
        {
            StartSceneId = "a",
            Scenes =
            [
                Scene("a", new StoryChoice
                {
                    Id = "door",
                    Text = "Kapıyı aç",
                    NextSceneId = "b",
                    Conditions = [new StoryCondition { Type = "SkillCondition", Key = "Güç", Operator = ">=", Amount = 90 }]
                }),
                Scene("b")
            ]
        };
        var engine = new StoryEngine(story, [Character("hero")]);
        var state = State();

        engine.ChooseWithAssistance(state, "door", "Ambatukam");

        Assert.Equal("b", state.CurrentSceneId);
        Assert.Contains("Ambatukam", state.LastChoiceText);
    }

    [Fact]
    public void ItemDurabilityEffectRemovesBrokenTool()
    {
        var state = State();
        state.Inventory.Add(new ItemState { Id = "crowbar", Name = "Levye", Durability = 10 });

        new EffectProcessor().Apply(new StoryEffect { Type = "ChangeItemDurability", Key = "crowbar", Amount = -10 }, state);

        Assert.Empty(state.Inventory);
    }

    [Fact]
    public void UnknownConditionAndEffectDoNotThrow()
    {
        var diagnostics = new RecordingDiagnostics();
        var state = State();

        var result = new ConditionEvaluator(diagnostics).Evaluate(
            new StoryCondition { Type = "Unknown" },
            state,
            Characters());
        new EffectProcessor(diagnostics).Apply(new StoryEffect { Type = "Unknown" }, state);

        Assert.False(result);
        Assert.Equal(2, diagnostics.Warnings.Count);
    }

    [Fact]
    public void LiveClockAdvancesOneGameDayInTenRealMinutes()
    {
        var state = State();
        state.Day = 2;
        state.Time = TimeSpan.Zero;

        for (var second = 0; second < 600; second++)
        {
            GameClock.AdvanceRealSecond(state);
        }

        Assert.Equal(3, state.Day);
        Assert.Equal(TimeSpan.Zero, state.Time);
    }

    [Fact]
    public void SceneMetadataDoesNotMoveLiveClockBackwards()
    {
        var story = new StoryPackage
        {
            StartSceneId = "a",
            Scenes =
            [
                Scene("a", new StoryChoice { Id = "go", Text = "Git", NextSceneId = "b" }),
                new StoryScene
                {
                    Id = "b",
                    Title = "b",
                    Day = 1,
                    Time = "22:50",
                    Content = [new StoryBlock { Text = "b" }]
                }
            ]
        };
        var state = State();
        state.Day = 2;
        state.Time = new TimeSpan(3, 15, 0);

        new StoryEngine(story, [Character("hero")]).Choose(state, "go");

        Assert.Equal(2, state.Day);
        Assert.Equal(new TimeSpan(3, 15, 0), state.Time);
    }

    [Fact]
    public void ClonePreservesDetailedTasksAndFatigue()
    {
        var state = State();
        state.Fatigue = 47;
        state.Tasks.Add(new TaskState
        {
            Id = "radio",
            Title = "Telsizi çalıştır",
            Description = "Frekansı yakala",
            Hint = "Pil bul",
            Priority = "Yüksek",
            Progress = 60
        });
        state.Messages.Add(new CharacterMessageState
        {
            CharacterId = "bedo",
            CharacterName = "Bedo",
            Text = "Buradayım.",
            SceneId = "a"
        });

        var clone = GameStateCloner.Clone(state, includeTransient: false);

        Assert.Equal(47, clone.Fatigue);
        Assert.Equal(60, Assert.Single(clone.Tasks).Progress);
        Assert.Equal("Pil bul", clone.Tasks[0].Hint);
        Assert.Equal("Buradayım.", Assert.Single(clone.Messages).Text);
    }

    [Fact]
    public void NightDirectorServiceCreatesInitialState()
    {
        var service = new NightDirectorService();

        service.Initialize(123, "season-01", "chapter-01");
        var state = service.GetState();

        Assert.Equal(123, state.DirectorSeed);
        Assert.Equal("season-01", state.CurrentSeasonId);
        Assert.Equal("chapter-01", state.CurrentChapterId);
    }

    [Fact]
    public void NightDirectorDeterministicRandomRepeatsForSameSeedAndScene()
    {
        var first = new NightDirectorService();
        first.Initialize(123, "season-01", "chapter-01");
        first.OnSceneEntered(Scene("scene-01"));

        var second = new NightDirectorService();
        second.Initialize(123, "season-01", "chapter-01");
        second.OnSceneEntered(Scene("scene-01"));

        Assert.Equal(first.CreateDeterministicRandom("choice-a").Next(), second.CreateDeterministicRandom("choice-a").Next());
    }

    [Fact]
    public void NightDirectorNoiseRaisesThreat()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        var before = service.GetState().ThreatScore;

        service.UpdateNoise(40, "metal-door");

        Assert.True(service.GetState().ThreatScore > before);
        Assert.Equal("metal-door", service.GetState().LastNoiseSource);
    }

    [Fact]
    public void NightDirectorDecayReducesNoise()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        service.UpdateNoise(40, "metal-door");
        var before = service.GetState().NoiseLevel;

        service.DecayNoise();

        Assert.True(service.GetState().NoiseLevel < before);
        Assert.True(service.GetState().LocalNoiseLevel < before);
    }

    [Fact]
    public void NightDirectorNightBonusRaisesThreat()
    {
        var day = new NightDirectorService();
        day.Initialize(123, "season-01", "chapter-01");
        day.OnSceneEntered(new StoryScene { Id = "day", Title = "Day", Time = "12:00", IsNight = false, InfectedDensityHint = 15 });

        var night = new NightDirectorService();
        night.Initialize(123, "season-01", "chapter-01");
        night.OnSceneEntered(new StoryScene { Id = "night", Title = "Night", Time = "23:00", IsNight = true, InfectedDensityHint = 15 });

        Assert.True(night.GetState().ThreatScore > day.GetState().ThreatScore);
    }

    [Theory]
    [InlineData(0, ThreatLevel.Safe)]
    [InlineData(30, ThreatLevel.Low)]
    [InlineData(55, ThreatLevel.Medium)]
    [InlineData(79, ThreatLevel.High)]
    [InlineData(80, ThreatLevel.Critical)]
    [InlineData(100, ThreatLevel.Critical)]
    public void NightDirectorThreatScoreMapsToThreatLevel(int score, ThreatLevel expected)
    {
        Assert.Equal(expected, NightDirectorService.ScoreToThreatLevel(score));
    }

    [Fact]
    public void NightDirectorNoiseClampsAndTracksRegionalNoise()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");

        service.AddNoise("gunshot", 200, "street");
        service.AddRegionalNoise("alley", "alarm", 35);

        Assert.Equal(100, service.GetState().NoiseLevel);
        Assert.Equal(100, service.GetState().RegionalNoiseLevels["street"]);
        Assert.Equal(35, service.GetState().RegionalNoiseLevels["alley"]);
        Assert.NotEmpty(service.GetState().ActiveNoiseSources);
        Assert.NotEmpty(service.GetState().NoiseHistory);
    }

    [Fact]
    public void NightDirectorDistractionCreatesNoiseAwayFromCurrentLocation()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        service.GetState().CurrentLocation = "market";
        service.AddNoise("running", 20, "market");
        var beforeThreat = service.GetState().ThreatScore;

        service.CreateDistraction("opposite_street", 35);

        Assert.True(service.GetState().RegionalNoiseLevels["opposite_street"] >= 35);
        Assert.Contains(service.GetState().ActiveNoiseSources, source => source.Location == "opposite_street");
        Assert.True(service.GetState().ThreatScore <= beforeThreat);
    }

    [Fact]
    public void NightDirectorChoiceNoiseThreatAndDistractionFieldsApply()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        service.GetState().CurrentLocation = "market";
        var choice = new StoryChoice
        {
            Id = "choice_throw_bottle",
            Text = "Şişeyi karşı sokağa fırlat.",
            NoiseSource = "distraction_throw",
            NoiseChange = 18,
            CreatesDistraction = true,
            DistractionLocation = "opposite_street",
            DistractionStrength = 35,
            ThreatChange = -8,
            TensionChange = 5
        };

        service.ApplyChoiceNoiseAndThreat(choice);

        Assert.Equal("distraction_throw", service.GetState().LastNoiseSource);
        Assert.True(service.GetState().RegionalNoiseLevels["opposite_street"] >= 35);
        Assert.Contains(5, service.GetState().RecentTensionChanges);
    }

    [Fact]
    public void NightDirectorTensionClampsAndRisesWithThreatAndNoise()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        var before = service.GetState().TensionLevel;

        service.AddNoise("alarm", 70, "street");
        service.ApplyTensionDelta(200, "test");

        Assert.True(service.GetState().TensionLevel > before);
        Assert.Equal(100, service.GetState().TensionLevel);
    }

    [Fact]
    public void NightDirectorMajorEventStartsRecoveryAndReducesMajorWeight()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        service.GetState().TensionLevel = 70;
        var directorEvent = Event("major_attack", isMajor: true, weight: 40);
        var before = service.CalculateEventWeight(directorEvent);

        service.OnMajorEventTriggered(directorEvent.Id);
        var after = service.CalculateEventWeight(directorEvent);

        Assert.Equal(PacingState.Recovery, service.GetPacingState());
        Assert.True(service.GetState().RecoveryScenesRemaining > 0);
        Assert.True(after < before);
    }

    [Fact]
    public void NightDirectorEventEligibilityHonorsCooldownOccurrenceNightAndFlags()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        service.GetState().IsNight = false;
        service.GetState().TensionLevel = 35;
        service.GetState().ThreatScore = 35;
        service.LoadDirectorEvents(
        [
            Event("night_only", requiresNight: true),
            Event("needs_flag", requiredFlags: ["radio_on"]),
            Event("blocked_flag", blockedFlags: ["door_open"]),
            Event("normal", maxOccurrences: 1)
        ]);
        service.RegisterEventOccurrence("normal");
        service.GetState().EventCooldowns["normal"] = 0;
        service.GetState().Flags.Add("door_open");

        var eligible = service.GetEligibleEvents(Scene("event-scene")).Select(item => item.Id).ToList();

        Assert.DoesNotContain("night_only", eligible);
        Assert.DoesNotContain("needs_flag", eligible);
        Assert.DoesNotContain("blocked_flag", eligible);
        Assert.DoesNotContain("normal", eligible);
    }

    [Fact]
    public void NightDirectorChoosesSameEventForSameSeedAndState()
    {
        var first = ServiceWithEvents();
        var second = ServiceWithEvents();
        var scene = Scene("event-scene");

        var firstEvent = first.ChooseEventDeterministically(scene);
        var secondEvent = second.ChooseEventDeterministically(scene);

        Assert.NotNull(firstEvent);
        Assert.Equal(firstEvent!.Id, secondEvent!.Id);
    }

    [Fact]
    public void NightDirectorUnknownEffectDoesNotThrowAndLogs()
    {
        var diagnostics = new RecordingDiagnostics();
        var service = new NightDirectorService(diagnostics);
        service.Initialize(123, "season-01", "chapter-01");
        var directorEvent = new DirectorEvent
        {
            Id = "unknown_effect",
            Weight = 10,
            Effects = [new DirectorEventEffect { Type = "mysteryEffect", Amount = 1 }]
        };

        var result = service.ApplyDirectorEvent(directorEvent);

        Assert.Equal("unknown_effect", result.EventId);
        Assert.Contains(diagnostics.Warnings, warning => warning.Contains("Bilinmeyen Director event effect"));
    }

    [Fact]
    public void NightDirectorSceneRulesCanBlockEventsAndMajorEvents()
    {
        var service = ServiceWithEvents();
        var blocked = new StoryScene
        {
            Id = "blocked",
            Title = "Blocked",
            DirectorRules = new DirectorRules { AllowDirectorEvents = false }
        };
        var noMajor = new StoryScene
        {
            Id = "no-major",
            Title = "No Major",
            DirectorRules = new DirectorRules { ForceNoMajorEvent = true }
        };

        Assert.Empty(service.GetEligibleEvents(blocked));
        Assert.DoesNotContain(service.GetEligibleEvents(noMajor), item => item.IsMajor);
    }

    [Fact]
    public void NightDirectorDecaysNoiseAndAppliesNightMultipliers()
    {
        var state = State();
        state.IsNight = true;
        state.NoiseLevel = 60;
        state.ThreatLevel = 30;
        state.Director.LocalNoiseLevel = 40;

        NightDirector.AdvanceScene(state, Scene("a"));

        Assert.True(state.NoiseLevel < 60);
        Assert.Equal(1.45, state.Director.NightThreatMultiplier);
        Assert.Equal(1.30, state.Director.NightNoiseSensitivityMultiplier);
        Assert.NotEqual("Low", state.Director.ThreatBand);
    }

    [Fact]
    public void NightDirectorRecordsPlayerStyleAndLocalNoise()
    {
        var state = State();
        state.Location = "Apartman";
        var choice = new StoryChoice
        {
            Id = "force_door",
            Text = "Kapıyı zorla",
            IsDangerous = true,
            Effects = [new StoryEffect { Type = "ChangeNoise", Amount = 18 }]
        };

        NightDirector.RecordChoice(state, choice);

        Assert.Equal(1, state.Director.PlayerStyleScores["Risk"]);
        Assert.True(state.Director.LocalNoiseLevel >= 18);
        Assert.Equal("force_door", state.Director.LastNoiseSource);
        Assert.Equal("Apartman", state.Director.NoiseLocation);
    }

    [Fact]
    public void NightDirectorCooldownPreventsBackToBackSameEvent()
    {
        var state = State();
        state.NoiseLevel = 95;
        state.ThreatLevel = 95;
        state.Fatigue = 80;
        state.Morale = 15;

        var first = NightDirector.AdvanceScene(state, Scene("a"));
        var cooldownsAfterFirst = state.Director.EventCooldowns.Count;
        var firstEvent = first?.EventId ?? state.Director.LastEventId;
        NightDirector.AdvanceScene(state, Scene("b"));

        if (!string.IsNullOrWhiteSpace(firstEvent))
        {
            Assert.True(cooldownsAfterFirst > 0);
            Assert.NotEqual(firstEvent, state.Director.LastEventId);
        }
    }

    [Fact]
    public void NightDirectorStateClonesDeterministically()
    {
        var state = State();
        state.Director.Seed = 1234;
        state.Director.EventCooldowns["infected_patrol"] = 3;
        state.Director.PlayerStyleScores["Planli"] = 2;
        state.ListenedVoiceMessages.Add("radio_01");

        var clone = GameStateCloner.Clone(state, includeTransient: false);

        Assert.Equal(1234, clone.Director.Seed);
        Assert.Equal(3, clone.Director.EventCooldowns["infected_patrol"]);
        Assert.Equal(2, clone.Director.PlayerStyleScores["Planli"]);
        Assert.Contains("radio_01", clone.ListenedVoiceMessages);
    }

    [Fact]
    public void NightDirectorHelpDecisionUsesTrustAndDeterministicRoll()
    {
        var state = State();
        state.Director.Seed = 99;
        state.CurrentSceneId = "door";
        var helper = new CharacterState { Id = "bedo", Name = "Bedo", Injury = "Yok" };

        var trusted = NightDirector.ShouldHelperAccept(state, new RelationshipState { CharacterId = "bedo", Trust = 90 }, helper, 95);
        var untrusted = NightDirector.ShouldHelperAccept(state, new RelationshipState { CharacterId = "bedo", Trust = 5 }, helper, 5);

        Assert.True(trusted);
        Assert.False(untrusted);
    }

    [Fact]
    public void CharacterHelpDecisionImprovesWithHighTrust()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        var state = State();
        var helper = new CharacterState { Id = "ambatukam", Name = "Ambatukam", Mood = "Sakin", Injury = "Yok" };
        var definition = Character("ambatukam", new Dictionary<string, int> { ["Güç"] = 90 });

        var high = service.DecideCharacterHelp(state, definition, helper, new RelationshipState { CharacterId = "ambatukam", Trust = 90 }, HelpRequest("break_door"));
        var low = service.DecideCharacterHelp(state, definition, helper, new RelationshipState { CharacterId = "ambatukam", Trust = 5 }, HelpRequest("break_door"));

        Assert.True(high.Score > low.Score);
        Assert.True(high.Result is CharacterHelpResult.Accept or CharacterHelpResult.ReluctantAccept or CharacterHelpResult.ConditionalAccept);
        Assert.True((int)low.Result > (int)high.Result);
    }

    [Fact]
    public void CharacterHelpDecisionCanRefuseWhenInjuredAndExhausted()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        service.GetState().ThreatScore = 80;
        service.GetState().TensionLevel = 80;
        var state = State();
        state.Fatigue = 95;
        state.Hunger = 80;
        state.Thirst = 80;
        var helper = new CharacterState { Id = "bedo", Name = "Bedo", Mood = "Korkmuş", Injury = "Bacak yarası" };
        var definition = Character("bedo", new Dictionary<string, int> { ["Çeviklik"] = 45 });

        var decision = service.DecideCharacterHelp(state, definition, helper, new RelationshipState { CharacterId = "bedo", Trust = 40 }, HelpRequest("scout", "Çeviklik", 80, 80));

        Assert.True(decision.Result is CharacterHelpResult.Refuse or CharacterHelpResult.AngryRefuse or CharacterHelpResult.SuggestAlternative);
    }

    [Fact]
    public void PlayerStyleEffectsAccumulateMultipleProfiles()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");

        service.ApplyChoiceNoiseAndThreat(new StoryChoice
        {
            Id = "quiet_risk",
            Text = "Sessiz ama riskli ilerle",
            PlayerStyleEffects = { ["Silent"] = 2, ["RiskTaker"] = 1, ["Aggressive"] = -1 }
        });

        Assert.Equal(2, service.GetState().PlayerStyleScores["Silent"]);
        Assert.Equal(1, service.GetState().PlayerStyleScores["RiskTaker"]);
        Assert.Equal(-1, service.GetState().PlayerStyleScores["Aggressive"]);
        Assert.Contains("Silent", service.GetDominantPlayerStyles());
    }

    [Theory]
    [InlineData(85, "Destekleyici", "Yüksek")]
    [InlineData(50, "Temkinli", "Orta")]
    [InlineData(25, "Mesafeli", "Düşük")]
    [InlineData(5, "Sert", "Öfkeli")]
    public void TeamMessageToneChangesWithTrust(int trust, string tone, string trustLevel)
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        var helper = new CharacterState { Id = "yesking", Name = "Yesking", Mood = "Temkinli" };

        var message = service.CreateTeamMessage(Character("yesking"), helper, new RelationshipState { CharacterId = "yesking", Trust = trust }, "scene", 1, "22:45");

        Assert.Equal(tone, message.Tone);
        Assert.Equal(trustLevel, message.TrustLevel);
        Assert.False(string.IsNullOrWhiteSpace(message.Text));
    }

    private static StoryScene Scene(string id, params StoryChoice[] choices) => new()
    {
        Id = id,
        Title = id,
        Content = [new StoryBlock { Text = id }],
        Choices = [.. choices]
    };

    private static GameState State() => new()
    {
        SelectedCharacterId = "hero",
        CurrentSceneId = "a"
    };

    private static CharacterHelpRequest HelpRequest(string type, string skill = "Güç", int difficulty = 45, int danger = 30) => new()
    {
        CharacterId = "helper",
        RequestType = type,
        RequiredSkill = skill,
        Difficulty = difficulty,
        DangerLevel = danger
    };

    private static NightDirectorService ServiceWithEvents()
    {
        var service = new NightDirectorService();
        service.Initialize(123, "season-01", "chapter-01");
        service.GetState().TensionLevel = 55;
        service.GetState().ThreatScore = 45;
        service.LoadDirectorEvents(
        [
            Event("minor-a", weight: 10),
            Event("minor-b", weight: 15),
            Event("major-a", isMajor: true, weight: 20)
        ]);
        return service;
    }

    private static DirectorEvent Event(
        string id,
        bool isMajor = false,
        bool requiresNight = false,
        int weight = 10,
        int maxOccurrences = 3,
        List<string>? requiredFlags = null,
        List<string>? blockedFlags = null) => new()
        {
            Id = id,
            Title = id,
            Description = id,
            Weight = weight,
            IsMajor = isMajor,
            IsMinor = !isMajor,
            RequiresNight = requiresNight,
            MaxOccurrences = maxOccurrences,
            RequiredFlags = requiredFlags ?? [],
            BlockedFlags = blockedFlags ?? []
        };

    private static IReadOnlyDictionary<string, CharacterDefinition> Characters() =>
        new Dictionary<string, CharacterDefinition> { ["hero"] = Character("hero") };

    private static CharacterDefinition Character(string id, Dictionary<string, int>? stats = null) => new()
    {
        Id = id,
        Name = id,
        Gender = "Erkek",
        Profession = "Test",
        Specialty = "Test",
        Role = "Test",
        Personality = "Test",
        Fear = "Test",
        SpecialAbility = "Test",
        SpecialAbilityDescription = "Test",
        HiddenPast = "Test",
        Stats = stats ?? []
    };

    private sealed class RecordingDiagnostics : IGameDiagnostics
    {
        public List<string> Warnings { get; } = [];
        public void Warning(string message) => Warnings.Add(message);
        public void Error(string message) { }
    }
}
