using System.Text.Json.Serialization;

namespace ZombieNightProtocol.Core;

public static class GameConstants
{
    public const string ApplicationName = "Zombie Night Protocol";
    public const string Version = "1.0.0";
    public const int SaveSlotCount = 3;
}

public enum ItemCategory
{
    Health,
    Food,
    Drink,
    Tool,
    Weapon,
    Ammunition,
    Quest,
    Other
}

public enum EndingKind
{
    None,
    Death,
    ChapterComplete,
    GameComplete
}

public enum ThreatLevel
{
    Safe,
    Low,
    Medium,
    High,
    Critical
}

public enum PacingState
{
    Calm,
    Uneasy,
    Rising,
    High,
    Recovery
}

public enum CharacterHelpResult
{
    Accept,
    ReluctantAccept,
    ConditionalAccept,
    SuggestAlternative,
    Refuse,
    AngryRefuse
}

public sealed class CharacterDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Gender { get; init; }
    public int Age { get; init; }
    public required string Profession { get; init; }
    public required string Specialty { get; init; }
    public required string Role { get; init; }
    public string CardDescription { get; init; } = "";
    public required string Personality { get; init; }
    public required string Fear { get; init; }
    public required string SpecialAbility { get; init; }
    public required string SpecialAbilityDescription { get; init; }
    public required string HiddenPast { get; init; }
    public string GameplayAdvantage { get; init; } = "";
    public string PlayStyle { get; init; } = "";
    public List<string> Strengths { get; init; } = [];
    public List<string> Weaknesses { get; init; } = [];
    public List<ItemState> StartingItems { get; init; } = [];
    public Dictionary<string, int> Stats { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CharacterState
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsPlayer { get; set; }
    public bool IsAlive { get; set; } = true;
    public int Health { get; set; } = 100;
    public int Infection { get; set; }
    public string Mood { get; set; } = "Temkinli";
    public string LastThought { get; set; } = "Sana söylemek istemediği bir şey var.";
    public string Injury { get; set; } = "Yok";
    public bool HiddenInfoUnlocked { get; set; }
    public bool SpecialAbilityUsed { get; set; }
}

public sealed class ItemState
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public ItemCategory Category { get; init; }
    public int Quantity { get; set; } = 1;
    public double Weight { get; init; }
    public bool IsUsable { get; init; } = true;
    public bool IsQuestItem { get; init; }
    public bool IsConsumable { get; init; }
    public int Damage { get; init; }
    public int Healing { get; init; }
    public int Durability { get; set; } = 100;
    public string StoryTag { get; init; } = "";
}

public sealed class RelationshipState
{
    public required string CharacterId { get; init; }
    public int Trust { get; set; } = 50;
    public string Status => Trust switch
    {
        >= 80 => "Tam Güven",
        >= 60 => "Güveniyor",
        >= 40 => "Temkinli",
        >= 20 => "Şüpheli",
        _ => "Düşmanca"
    };
}

public sealed class JournalEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string Text { get; init; }
    public string Category { get; init; } = "Hikâye";
    public string Location { get; init; } = "";
    public int Day { get; init; } = 1;
    public string GameTime { get; init; } = "";
}

public sealed class TaskState
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public string Hint { get; init; } = "";
    public string Priority { get; init; } = "Normal";
    public int Progress { get; set; }
    public bool IsCompleted { get; set; }
}

public sealed class CharacterMessageState
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string CharacterId { get; init; }
    public required string CharacterName { get; init; }
    public required string Text { get; init; }
    public string Tone { get; init; } = "Bilgi";
    public string TrustLevel { get; init; } = "Orta";
    public string Mood { get; init; } = "Temkinli";
    public string RelationshipContext { get; init; } = "Temkinli";
    public int Day { get; init; } = 1;
    public string GameTime { get; init; } = "";
    public string RelatedVoiceMessageId { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public bool IsRead { get; set; }
    public string SceneId { get; init; } = "";
}

public sealed class GameState
{
    public int SaveVersion { get; init; } = 1;
    public string SelectedCharacterId { get; set; } = "";
    public string CurrentSceneId { get; set; } = "";
    public string CurrentChapterId { get; set; } = "chapter_01";
    public string City { get; set; } = "İstanbul";
    public string Location { get; set; } = "Esenler";
    public int Day { get; set; } = 1;
    public TimeSpan Time { get; set; } = new(22, 45, 0);
    public bool IsNight { get; set; } = true;
    public int Health { get; set; } = 100;
    public int Hunger { get; set; } = 20;
    public int Thirst { get; set; } = 25;
    public int Sanity { get; set; } = 85;
    public int Morale { get; set; } = 70;
    public int Fatigue { get; set; } = 18;
    public int Infection { get; set; }
    public int NoiseLevel { get; set; } = 15;
    public int ThreatLevel { get; set; } = 35;
    public int InfectionRisk { get; set; } = 10;
    public long PlayedSeconds { get; set; }
    public DateTimeOffset LastSavedAt { get; set; }
    public EndingKind Ending { get; set; }
    public string EndingReason { get; set; } = "";
    public string LastChoiceText { get; set; } = "";
    public int ChoiceCount { get; set; }
    public bool CanUndo { get; set; }
    public bool SessionInProgress { get; set; }
    public string? CheckpointSceneId { get; set; }
    public List<ItemState> Inventory { get; init; } = [];
    public List<CharacterState> Characters { get; init; } = [];
    public List<RelationshipState> Relationships { get; init; } = [];
    public HashSet<string> Flags { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Counters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ChoiceHistory { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UnlockedRoutes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<JournalEntry> Journal { get; init; } = [];
    public List<TaskState> Tasks { get; init; } = [];
    public List<CharacterMessageState> Messages { get; init; } = [];
    public HashSet<string> VisitedScenes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ListenedVoiceMessages { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PersistentChoiceState> PersistentChoices { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CharacterMemoryState> CharacterMemories { get; init; } = [];
    public HashSet<string> Promises { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> EnemyGroups { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AlliedGroups { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> RouteSelections { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public VehicleState Vehicle { get; set; } = new();
    public NightDirectorState Director { get; set; } = new();

    [JsonIgnore]
    public GameState? UndoSnapshot { get; set; }

    [JsonIgnore]
    public GameState? CheckpointSnapshot { get; set; }
}

public sealed class StoryPackage
{
    public string Id { get; init; } = "prototype-tr";
    public string Version { get; init; } = "1.0.0";
    public required string StartSceneId { get; init; }
    public List<StoryScene> Scenes { get; init; } = [];
}

public sealed class SeasonDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public int Order { get; init; }
    public string Language { get; init; } = "tr";
    public string StartChapterId { get; init; } = "";
    public List<SeasonChapterReference> Chapters { get; init; } = [];
    public string SharedCharactersPath { get; init; } = "";
    public string SharedDirectorRulesPath { get; init; } = "";
    public CarryOverRules CarryOverRules { get; init; } = new();
    public bool IsPrototype { get; init; }
    public bool IsDemo { get; init; }
}

public sealed class SeasonChapterReference
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public int Order { get; init; }
    public string ChapterPath { get; init; } = "";
}

public sealed class CarryOverRules
{
    public bool MainCharacter { get; init; } = true;
    public bool CharacterLifeState { get; init; } = true;
    public bool TrustValues { get; init; } = true;
    public bool Injuries { get; init; } = true;
    public bool Infection { get; init; } = true;
    public bool Inventory { get; init; } = true;
    public bool Tasks { get; init; } = true;
    public bool PlayerStyle { get; init; } = true;
    public bool ImportantChoices { get; init; } = true;
    public bool Promises { get; init; } = true;
    public bool Groups { get; init; } = true;
    public bool ListenedVoiceMessages { get; init; } = true;
    public bool MessageHistory { get; init; } = true;
    public bool VehicleState { get; init; } = true;
    public bool RouteSelections { get; init; } = true;
    public bool NightDirectorHistory { get; init; } = true;
}

public sealed class ChapterDefinition
{
    public required string Id { get; init; }
    public string SeasonId { get; init; } = "";
    public required string Title { get; init; }
    public int Order { get; init; }
    public string StartSceneId { get; init; } = "";
    public string SceneFile { get; init; } = "scenes.json";
    public string Summary { get; init; } = "";
    public List<StoryCondition> AvailableIf { get; init; } = [];
    public string DefaultCheckpointId { get; init; } = "";
}

public sealed class SeasonalScenesDocument
{
    public List<SeasonalScene> Scenes { get; init; } = [];
    public List<EndingDefinition> Endings { get; init; } = [];
    public List<CheckpointDefinition> Checkpoints { get; init; } = [];
}

public sealed class SeasonalScene
{
    public required string Id { get; init; }
    public string ChapterId { get; init; } = "";
    public required string Title { get; init; }
    public string Text { get; init; } = "";
    public string? SceneImage { get; init; }
    public VoiceMessageDefinition? VoiceMessage { get; init; }
    public List<SeasonalChoice> Choices { get; init; } = [];
    public DirectorRules DirectorRules { get; init; } = new();
    public string CheckpointId { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public string Location { get; init; } = "";
    public string TimeOfDay { get; init; } = "";
    public string ThreatHint { get; init; } = "";
    public int NoiseBase { get; init; }
    public int InfectedDensityHint { get; init; }
    public string LightingHint { get; init; } = "";
    public int LightLevel { get; init; } = 50;
    public string Notes { get; init; } = "";
}

public sealed class SeasonalChoice
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public string? TargetSceneId { get; init; }
    public string ChoicePreviewImage { get; init; } = "";
    public string Summary { get; init; } = "";
    public List<StoryCondition> Requirements { get; init; } = [];
    public List<StoryEffect> Effects { get; init; } = [];
    public string PersistentChoiceId { get; init; } = "";
    public int NoiseChange { get; init; }
    public string NoiseSource { get; init; } = "";
    public string NoiseLocation { get; init; } = "";
    public bool CreatesDistraction { get; init; }
    public string DistractionLocation { get; init; } = "";
    public int DistractionStrength { get; init; }
    public int ThreatChange { get; init; }
    public int TensionChange { get; init; }
    public Dictionary<string, int> CharacterTrustEffects { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<InventoryEffectDefinition> InventoryEffects { get; init; } = [];
    public Dictionary<string, int> PlayerStyleEffects { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DirectorFlags { get; init; } = [];
    public bool IsTimed { get; init; }
    public int TimeLimitSeconds { get; init; }
    public bool LockRollback { get; init; }
}

public sealed class EndingDefinition
{
    public required string Id { get; init; }
    public string Type { get; init; } = "chapterComplete";
    public required string Title { get; init; }
    public string Text { get; init; } = "";
    public bool IsDeathEnding { get; init; }
    public string CheckpointToReturn { get; init; } = "";
    public bool CarryToNextChapter { get; init; }
    public bool CarryToNextSeason { get; init; }
}

public sealed class CheckpointDefinition
{
    public required string Id { get; init; }
    public string SceneId { get; init; } = "";
    public required string Title { get; init; }
    public bool SaveStateSnapshot { get; init; } = true;
    public bool CanReturnAfterDeath { get; init; } = true;
}

public sealed class StoryScene
{
    public required string Id { get; init; }
    public string ChapterId { get; init; } = "chapter_01";
    public string City { get; init; } = "İstanbul";
    public string Location { get; init; } = "Esenler";
    public int Day { get; init; } = 1;
    public string Time { get; init; } = "22:45";
    public bool IsNight { get; init; } = true;
    public required string Title { get; init; }
    public string Background { get; init; } = "fallback";
    public string SceneImage { get; init; } = "images/scenes/fallback.webp";
    public string BackgroundSceneImage { get; init; } = "";
    public string AmbientAudio { get; init; } = "";
    public string BackgroundMusic { get; init; } = "";
    public List<string> OneShotSounds { get; init; } = [];
    public VoiceMessageDefinition? VoiceMessage { get; set; }
    public DirectorRules DirectorRules { get; init; } = new();
    public List<string> Tags { get; init; } = [];
    public string TimeOfDay { get; init; } = "";
    public string ThreatHint { get; init; } = "";
    public int NoiseBase { get; init; }
    public int InfectedDensityHint { get; init; }
    public string LightingHint { get; init; } = "";
    public int LightLevel { get; init; } = 50;
    public double ScreenShakeIntensity { get; init; }
    public double NoiseIntensity { get; init; }
    public List<StoryBlock> Content { get; init; } = [];
    public List<TopMessageDefinition> TopMessages { get; init; } = [];
    public List<StoryChoice> Choices { get; init; } = [];
    public bool IsCheckpoint { get; init; }
    public int? TimedChoiceSeconds { get; init; }
    public string? DefaultChoiceId { get; init; }
}

public sealed class StoryBlock
{
    public string Type { get; init; } = "narration";
    public string Speaker { get; init; } = "";
    public required string Text { get; init; }
    public string Silhouette { get; init; } = "";
    public string DialogueImage { get; init; } = "";
}

public sealed class StoryChoice
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public string? NextSceneId { get; init; }
    public bool IsDangerous { get; init; }
    public bool IsCriticalTimed { get; init; }
    public string PreviewImage { get; init; } = "";
    public string SelectionSummary { get; init; } = "";
    public string RiskLevel { get; init; } = "Orta";
    public string LockedReason { get; init; } = "Bu seçenek için gerekli koşullar sağlanmıyor.";
    public List<StoryCondition> Conditions { get; init; } = [];
    public List<StoryEffect> Effects { get; init; } = [];
    public int NoiseChange { get; init; }
    public string NoiseSource { get; init; } = "";
    public string NoiseLocation { get; init; } = "";
    public bool CreatesDistraction { get; init; }
    public string DistractionLocation { get; init; } = "";
    public int DistractionStrength { get; init; }
    public int ThreatChange { get; init; }
    public int TensionChange { get; init; }
    public Dictionary<string, int> PlayerStyleEffects { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DirectorFlags { get; init; } = [];
    public bool LockRollback { get; init; }
}

public sealed class TopMessageDefinition
{
    public required string Speaker { get; init; }
    public required string Text { get; init; }
    public string Tone { get; init; } = "Bilgi";
}

public sealed class VoiceMessageDefinition
{
    public required string Id { get; init; }
    public string SpeakerId { get; init; } = "";
    public required string Title { get; init; }
    public string AudioPath { get; init; } = "";
    public string Transcript { get; init; } = "";
    public bool AutoPlay { get; init; }
    public bool RequiredToContinue { get; init; }
    public bool RadioFilter { get; init; }
    public bool PhoneFilter { get; init; }
    public double VolumeMultiplier { get; init; } = 1;
    public bool RememberAsListened { get; init; } = true;
    public bool AddToMessageHistory { get; init; } = true;
}

public sealed class PersistentChoiceState
{
    public required string Id { get; init; }
    public string Label { get; init; } = "";
    public string Value { get; set; } = "";
    public List<string> RememberedByCharacters { get; init; } = [];
    public bool AffectsFutureChapters { get; init; }
    public bool AffectsFutureSeasons { get; init; }
}

public sealed class CharacterMemoryState
{
    public required string CharacterId { get; init; }
    public required string MemoryId { get; init; }
    public string SourceChoiceId { get; init; } = "";
    public string Tone { get; init; } = "Neutral";
    public int TrustImpact { get; init; }
    public List<string> FutureMessageTags { get; init; } = [];
}

public sealed class InventoryEffectDefinition
{
    public string Type { get; init; } = "";
    public string ItemId { get; init; } = "";
    public int Amount { get; init; }
}

public sealed class VehicleState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Fuel { get; set; }
    public int Condition { get; set; } = 100;
    public List<string> Tags { get; init; } = [];
}

public sealed class DirectorRules
{
    public int NoiseMultiplier { get; init; }
    public int ThreatMultiplier { get; init; }
    public int TensionChange { get; init; }
    public int CooldownSeconds { get; init; }
    public List<string> EventTags { get; init; } = [];
    public bool AllowDirectorEvents { get; init; } = true;
    public List<string> AllowedEventTags { get; init; } = [];
    public List<string> BlockedEventTags { get; init; } = [];
    public bool ForceNoMajorEvent { get; init; }
    public bool ForceCalm { get; init; }
    public int MinimumTension { get; init; }
    public int MaximumTension { get; init; } = 100;
    public double EventWeightMultiplier { get; init; } = 1;
}

public sealed class NoiseSourceState
{
    public required string Source { get; init; }
    public string Location { get; init; } = "";
    public int Strength { get; set; }
    public int RemainingScenes { get; set; } = 1;
}

public sealed class NoiseHistoryEntry
{
    public required string Source { get; init; }
    public string Location { get; init; } = "";
    public int Amount { get; init; }
    public int SceneIndex { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class NightDirectorSettings
{
    public double NightThreatMultiplier { get; init; } = 1.45;
    public double NightNoiseSensitivityMultiplier { get; init; } = 1.30;
    public double NightEventWeightMultiplier { get; init; } = 1.20;
}

public sealed class NightDirectorState
{
    public int DirectorSeed { get; set; }
    public int Seed
    {
        get => DirectorSeed;
        set => DirectorSeed = value;
    }
    public string CurrentSceneId { get; set; } = "";
    public string CurrentSeasonId { get; set; } = "";
    public string CurrentChapterId { get; set; } = "";
    public int SceneCounter { get; set; }
    public int LastMajorEventSceneIndex { get; set; } = -1000;
    public int LastCalmSceneIndex { get; set; }
    public int ScenesSinceLastMajorEvent { get; set; }
    public int ScenesSinceLastRest { get; set; }
    public int TensionLevel { get; set; }
    public List<int> RecentTensionChanges { get; init; } = [];
    public PacingState CurrentPacingState { get; set; } = PacingState.Calm;
    public int PressureBudget { get; set; } = 35;
    public int RecoveryScenesRemaining { get; set; }
    public int MajorEventCooldown { get; set; }
    public int MinorEventCooldown { get; set; }
    public int NoiseLevel { get; set; }
    public int LocalNoiseLevel { get; set; }
    public int ThreatScore { get; set; }
    public ThreatLevel ThreatLevel { get; set; } = ThreatLevel.Low;
    public bool IsNight { get; set; } = true;
    public string CurrentLocation { get; set; } = "";
    public int NearbyInfectedEstimate { get; set; }
    public List<string> RegionTags { get; init; } = [];
    public string LastNoiseSource { get; set; } = "";
    public string LastNoiseLocation { get; set; } = "";
    public DateTimeOffset? LastNoiseTimestamp { get; set; }
    public List<NoiseSourceState> ActiveNoiseSources { get; init; } = [];
    public Dictionary<string, int> RegionalNoiseLevels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int NoiseDecayRate { get; set; } = 4;
    public List<NoiseHistoryEntry> NoiseHistory { get; init; } = [];
    public string NoiseLocation { get; set; } = "";
    public string ThreatBand { get; set; } = "Low";
    public double NightThreatMultiplier { get; set; } = 1.15;
    public double NightNoiseSensitivityMultiplier { get; set; } = 1.2;
    public double NightEventWeightMultiplier { get; set; } = 1.2;
    public string LastMajorEventSceneId { get; set; } = "";
    public string LastEventId { get; set; } = "";
    public Dictionary<string, int> EventCooldowns { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> EventOccurrenceCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> PlayerStyleScores { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CharacterMoodHints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RecentEvents { get; init; } = [];
    public List<string> RecentEventIds => RecentEvents;
}

public sealed class DirectorEvent
{
    public required string Id { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public int MinimumNoise { get; init; }
    public int MaximumNoise { get; init; } = 100;
    public int MinimumTension { get; init; }
    public int MaximumTension { get; init; } = 100;
    public int MinimumThreat { get; init; }
    public int MaximumThreat { get; init; } = 100;
    public bool RequiresNight { get; init; }
    public bool RequiresDay { get; init; }
    public List<string> RequiredLocationTags { get; init; } = [];
    public List<string> BlockedLocationTags { get; init; } = [];
    public List<string> RequiredCharacterIds { get; init; } = [];
    public List<string> RequiredInventoryItems { get; init; } = [];
    public List<string> RequiredFlags { get; init; } = [];
    public List<string> BlockedFlags { get; init; } = [];
    public List<string> RequiredPreviousEvents { get; init; } = [];
    public List<string> BlockedPreviousEvents { get; init; } = [];
    public int CooldownScenes { get; init; } = 3;
    public int MaxOccurrences { get; init; } = 1;
    public int Weight { get; init; } = 10;
    public bool IsMajor { get; init; }
    public bool IsMinor { get; init; } = true;
    public List<DirectorEventEffect> Effects { get; init; } = [];
    public string MessageText { get; init; } = "";
    public string EventText { get; init; } = "";
    public List<string> AlternativeTexts { get; init; } = [];
}

public sealed class DirectorEventEffect
{
    public required string Type { get; init; }
    public string Target { get; init; } = "";
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
    public int Amount { get; init; }
}

public sealed class CharacterHelpRequest
{
    public required string CharacterId { get; init; }
    public required string RequestType { get; init; }
    public int Difficulty { get; init; } = 50;
    public int DangerLevel { get; init; } = 35;
    public string RequiredSkill { get; init; } = "";
    public string RequiredItem { get; init; } = "";
}

public sealed class CharacterHelpDecision
{
    public required string CharacterId { get; init; }
    public required string RequestType { get; init; }
    public int Difficulty { get; init; }
    public int DangerLevel { get; init; }
    public string RequiredSkill { get; init; } = "";
    public string RequiredItem { get; init; } = "";
    public int TrustModifier { get; init; }
    public int MoodModifier { get; init; }
    public int InjuryModifier { get; init; }
    public int FatigueModifier { get; init; }
    public int FearModifier { get; init; }
    public CharacterHelpResult Result { get; init; }
    public required string Message { get; init; }
    public List<StoryEffect> Effects { get; init; } = [];
    public int Score { get; init; }
}

public sealed class StoryCondition
{
    public required string Type { get; init; }
    public string Target { get; init; } = "";
    public string Key { get; init; } = "";
    public string Operator { get; init; } = "==";
    public string Value { get; init; } = "";
    public int Amount { get; init; }
}

public sealed class StoryEffect
{
    public required string Type { get; init; }
    public string Target { get; init; } = "";
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
    public int Amount { get; init; }
    public ItemState? Item { get; init; }
}

public sealed record ChoiceAvailability(StoryChoice Choice, bool IsAvailable, string LockedReason);

public sealed record StoryValidationIssue(string Severity, string Code, string Message);

public interface IGameDiagnostics
{
    void Warning(string message);
    void Error(string message);
}

public sealed class NullGameDiagnostics : IGameDiagnostics
{
    public void Warning(string message) { }
    public void Error(string message) { }
}

public interface IStoryRepository
{
    Task<StoryPackage> LoadAsync(CancellationToken cancellationToken = default);
}

public interface ICharacterRepository
{
    Task<IReadOnlyList<CharacterDefinition>> LoadAsync(CancellationToken cancellationToken = default);
}

public interface ISaveService
{
    Task<IReadOnlyList<SaveSlotSummary>> GetSlotsAsync(CancellationToken cancellationToken = default);
    Task<GameState?> LoadAsync(int slot, CancellationToken cancellationToken = default);
    Task SaveAsync(int slot, GameState state, CancellationToken cancellationToken = default);
    Task DeleteAsync(int slot, CancellationToken cancellationToken = default);
}

public sealed record SaveSlotSummary(
    int Slot,
    bool IsEmpty,
    bool IsCorrupted,
    string CharacterId,
    string CharacterName,
    string Chapter,
    string City,
    TimeSpan PlayedTime,
    DateTimeOffset? SavedAt,
    int AliveMembers);

public interface ISettingsService
{
    Task<GameSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(GameSettings settings, CancellationToken cancellationToken = default);
    string SettingsFolder { get; }
    string LogsFolder { get; }
    string SavesFolder { get; }
}

public sealed class GameSettings
{
    public int MasterVolume { get; set; } = 80;
    public int MusicVolume { get; set; } = 55;
    public int AmbientVolume { get; set; } = 70;
    public int EffectsVolume { get; set; } = 75;
    public int VoiceVolume { get; set; } = 80;
    public int NotificationVolume { get; set; } = 75;
    public bool NotificationSounds { get; set; } = true;
    public string TextSpeed { get; set; } = "Normal";
    public bool FullScreen { get; set; }
    public string Resolution { get; set; } = "1280x800";
    public int UiScale { get; set; } = 100;
    public bool ScreenShake { get; set; } = true;
    public bool NoiseEffect { get; set; } = true;
    public bool TextAnimation { get; set; } = true;
    public string TextAnimationMode { get; set; } = "Daktilo";
    public bool TopMessageNotifications { get; set; } = true;
    public bool AutoSave { get; set; } = true;
    public string Language { get; set; } = "Türkçe";
    public string UpdateChannel { get; set; } = "stable";
}
