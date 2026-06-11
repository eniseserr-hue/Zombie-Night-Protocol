# Hikâye Formatı

Hikâye paketi `id`, `version`, `startSceneId` ve `scenes` alanlarından oluşur. Kimlikler küçük harf, sayı, `_` ve `-` kullanmalıdır; paket içinde benzersiz olmalıdır.

Her sahne şehir, konum, gün, saat, gece durumu, başlık, içerik blokları ve seçimler taşır. İçerik blokları `narration` veya `dialogue` türündedir.

## Seçimler

Seçimde `id`, `text`, isteğe bağlı `nextSceneId`, `conditions` ve `effects` bulunur. `timedChoiceSeconds` ve `defaultChoiceId` sahne seviyesinde süreli kararı tanımlar. `isCriticalTimed` olan seçim geri alınamaz.

## Condition Türleri

`StatCondition`, `InventoryCondition`, `ItemQuantityCondition`, `RelationshipCondition`, `CharacterAliveCondition`, `CharacterDeadCondition`, `FlagCondition`, `CounterCondition`, `LocationCondition`, `TimeCondition`, `IsNightCondition`, `HealthCondition`, `InfectionCondition`, `ChoiceHistoryCondition`, `SelectedCharacterCondition`, `SkillCondition`.

Operatörler: `==`, `!=`, `>`, `>=`, `<`, `<=`, `contains`, `notContains`.

## Effect Türleri

`AddItem`, `RemoveItem`, `ChangeItemQuantity`, `ChangeHealth`, `ChangeHunger`, `ChangeThirst`, `ChangeSanity`, `ChangeMorale`, `ChangeInfection`, `ChangeRelationship`, `ChangeCharacterMood`, `ChangeCharacterThought`, `InjureCharacter`, `HealCharacter`, `KillCharacter`, `ReviveCharacter`, `SetFlag`, `RemoveFlag`, `IncrementCounter`, `SetCounter`, `UnlockRoute`, `UnlockCharacterInfo`, `UseSpecialAbility`, `SetCheckpoint`, `AddJournalEntry`, `AddTask`, `CompleteTask`, `ChangeNoise`, `ChangeThreat`, `AdvanceTime`, `TriggerEnding`.

Şema `content/stories/story.schema.json` dosyasındadır. `StoryValidator` tekrar eden ID, eksik başlangıç, olmayan bağlantı, ulaşılamayan sahne ve döngüleri raporlar.
