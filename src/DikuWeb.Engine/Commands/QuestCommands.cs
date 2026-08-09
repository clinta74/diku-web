using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Quests;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Talking to quest givers, the journal, and turn-in (PLAN.md §4.9, §5.2b).
/// </summary>
/// <remarks>
/// The cache and save queue come from the <see cref="CommandContext"/> rather than from statics
/// captured at registration, for the reason set out on <see cref="ShopCommands"/>: a static made
/// the last-constructed registry the one every other registry read from.
/// </remarks>
public static class QuestCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "talk", 1, "talk <npc> (t) - speak with an NPC about quests", Talk));

        commands.Add(new CommandDefinition(
            "quests", 3, "quests - list your active quests", Quests));

        commands.Add(new CommandDefinition(
            "quest", 3, "quest <name> - show quest details", QuestDetail));
    }

    private static void Talk(CommandContext ctx)
    {
        if (ctx.Quests is null || !ctx.Quests.IsLoaded)
        {
            ctx.Reply("Quests are not available.");
            return;
        }

        if (!ctx.HasArgument)
        {
            ctx.Reply("Talk to whom?");
            return;
        }

        var npcName = ctx.Argument;
        var character = ctx.Actor.Character;

        // Find the mob in the current room
        var targetMob = NameMatch.Best(
            ctx.World.MobsIn(character.RoomKey), npcName, m => m.TemplateName, m => m.TemplateKey);

        if (targetMob is null)
        {
            ctx.Reply($"You don't see '{npcName}' here.");
            return;
        }

        // Find quests offered by this mob
        var offeredQuests = ctx.Quests.GetByGiverMobKey(targetMob.TemplateKey);

        if (offeredQuests.Count == 0)
        {
            ctx.Reply($"{targetMob.TemplateKey} has nothing to say about quests.");
            return;
        }

        var narrations = new List<string>();

        foreach (var quest in offeredQuests.OrderBy(q => q.SortOrder))
        {
            var questState = ctx.World.GetQuestState(character.Id, quest.Key);

            // Check prerequisites
            var prerequisitesMet = CheckPrerequisites(ctx.World, character.Id, quest);

            if (questState is null && prerequisitesMet)
            {
                // No quest row and prerequisites are met - offer the quest
                var offer = quest.Dialogue.TryGetValue("giverOffer", out var offerText)
                    ? offerText
                    : $"I have a job for you: {quest.Summary}";
                narrations.Add(offer);

                // Create an active quest row
                var newQuestState = new CharacterQuest
                {
                    CharacterId = character.Id,
                    QuestKey = quest.Key,
                    Status = QuestStatus.Active,
                    StartedAt = DateTimeOffset.UtcNow
                };
                ctx.World.SetQuestState(character.Id, quest.Key, newQuestState);

                // Persist the state change
                ctx.QuestSaveQueue?.Enqueue(new CharacterQuestSnapshot(
                    character.Id, quest.Key, QuestStatus.Active, DateTimeOffset.UtcNow, null, 0));
            }
            else if (questState?.Status == QuestStatus.Active)
            {
                // Quest is already active
                var inProgress = quest.Dialogue.TryGetValue("giverInProgress", out var inProgressText)
                    ? inProgressText
                    : $"Still working on {quest.Name}?";
                narrations.Add(inProgress);
            }
            else if (questState?.Status == QuestStatus.Completed && !quest.IsRepeatable)
            {
                // Quest is complete and non-repeatable
                var complete = quest.Dialogue.TryGetValue("giverComplete", out var completeText)
                    ? completeText
                    : $"You've already completed {quest.Name}.";
                narrations.Add(complete);
            }
            else if (questState?.Status == QuestStatus.Completed && quest.IsRepeatable)
            {
                // Repeatable quest can be re-offered
                var offer = quest.Dialogue.TryGetValue("giverOffer", out var offerText)
                    ? offerText
                    : $"I have another job for you: {quest.Summary}";
                narrations.Add(offer);

                // Reset the quest for re-acceptance
                var resetQuestState = new CharacterQuest
                {
                    CharacterId = character.Id,
                    QuestKey = quest.Key,
                    Status = QuestStatus.Active,
                    StartedAt = DateTimeOffset.UtcNow
                };
                ctx.World.SetQuestState(character.Id, quest.Key, resetQuestState);

                // Persist the state change
                ctx.QuestSaveQueue?.Enqueue(new CharacterQuestSnapshot(
                    character.Id, quest.Key, QuestStatus.Active, DateTimeOffset.UtcNow, null, questState.TimesCompleted));
            }
            else if (!prerequisitesMet)
            {
                // Prerequisites not met - silently skip
                continue;
            }
        }

        if (narrations.Count == 0)
        {
            ctx.Reply($"{targetMob.TemplateKey} has nothing to say to you about quests.");
            return;
        }

        foreach (var narration in narrations)
        {
            ctx.Reply(narration);
        }
    }

    private static void Quests(CommandContext ctx)
    {
        var character = ctx.Actor.Character;
        var questList = ctx.World.QuestsFor(character.Id);

        if (questList.Count == 0)
        {
            ctx.Reply("You have no quests.");
            return;
        }

        var active = questList.Where(q => q.Status == QuestStatus.Active).ToList();
        var completed = questList.Where(q => q.Status == QuestStatus.Completed).ToList();

        ctx.Reply("=== Your Quests ===");

        if (active.Count > 0)
        {
            ctx.Reply("Active:");
            foreach (var quest in active)
            {
                var questDef = ctx.Quests?.Get(quest.QuestKey);
                var summary = questDef?.Summary ?? "Unknown quest";
                ctx.Reply($"  {questDef?.Name ?? quest.QuestKey}: {summary}");
            }
        }

        if (completed.Count > 0)
        {
            ctx.Reply("Completed:");
            foreach (var quest in completed)
            {
                var questDef = ctx.Quests?.Get(quest.QuestKey);
                ctx.Reply($"  {questDef?.Name ?? quest.QuestKey}");
            }
        }
    }

    private static void QuestDetail(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Which quest?");
            return;
        }

        var questName = ctx.Argument;
        var character = ctx.Actor.Character;
        var questList = ctx.World.QuestsFor(character.Id);

        // Find quest by name or partial name match
        var questState = questList.FirstOrDefault(q =>
            ctx.Quests?.Get(q.QuestKey)?.Name.Contains(questName, StringComparison.OrdinalIgnoreCase) == true);

        if (questState is null)
        {
            ctx.Reply("You don't have that quest.");
            return;
        }

        var questDef = ctx.Quests?.Get(questState.QuestKey);
        if (questDef is null)
        {
            ctx.Reply("Quest information is unavailable.");
            return;
        }

        ctx.Reply($"=== {questDef.Name} ===");
        if (!string.IsNullOrEmpty(questDef.Description))
        {
            ctx.Reply(questDef.Description);
        }

        if (!string.IsNullOrEmpty(questDef.Summary))
        {
            ctx.Reply($"Objective: {questDef.Summary}");
        }

        // Show progress
        if (questState.Status == QuestStatus.Active)
        {
            var inventory = ctx.World.InventoryOf(character.Id);
            var count = inventory.Count(i => i.TemplateKey.Equals(questDef.RequiredItemKey, StringComparison.OrdinalIgnoreCase));
            ctx.Reply($"Progress: {count}/{questDef.RequiredCount} {questDef.RequiredItemKey}");
        }
        else if (questState.Status == QuestStatus.Completed)
        {
            ctx.Reply("Status: Completed");
        }

        // Show rewards
        ctx.Reply("Rewards:");
        if (questDef.RewardXp > 0)
        {
            ctx.Reply($"  {questDef.RewardXp} experience");
        }

        if (questDef.RewardGold > 0)
        {
            ctx.Reply($"  {questDef.RewardGold} gold");
        }

        if (!string.IsNullOrEmpty(questDef.RewardItemKey))
        {
            ctx.Reply($"  {questDef.RewardItemCount} x {questDef.RewardItemKey}");
        }
    }

    private static bool CheckPrerequisites(WorldState world, Guid characterId, Quest quest)
    {
        if (quest.PrerequisiteQuestKeys.Count == 0)
        {
            return true;
        }

        foreach (var prereqKey in quest.PrerequisiteQuestKeys)
        {
            var prereqState = world.GetQuestState(characterId, prereqKey);
            if (prereqState?.Status != QuestStatus.Completed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to handle a quest turn-in. Returns true if a quest was turned in, false if not.
    /// Called from the Give command to check for NPC quest turn-ins before player-to-player gives.
    /// </summary>
    public static bool TryTurnInQuest(CommandContext ctx, string itemName, string npcName)
    {
        if (ctx.Quests is null || !ctx.Quests.IsLoaded)
        {
            return false;
        }

        var character = ctx.Actor.Character;

        // Find the mob in the current room
        var targetMob = NameMatch.Best(
            ctx.World.MobsIn(character.RoomKey), npcName, m => m.TemplateName, m => m.TemplateKey);

        if (targetMob is null)
        {
            return false;
        }

        // Find quests that can be turned in to this mob
        var turnInQuests = ctx.Quests.GetByTurninMobKey(targetMob.TemplateKey);

        if (turnInQuests.Count == 0)
        {
            return false;
        }

        // Find a quest the character has that matches this item and NPC
        Quest? matchingQuest = null;
        foreach (var quest in turnInQuests)
        {
            var questState = ctx.World.GetQuestState(character.Id, quest.Key);
            if (questState?.Status == QuestStatus.Active &&
                string.Equals(quest.RequiredItemKey, itemName, StringComparison.OrdinalIgnoreCase))
            {
                matchingQuest = quest;
                break;
            }
        }

        if (matchingQuest is null)
        {
            return false;
        }

        // Count items in inventory that match the quest requirement
        var inventory = ctx.World.InventoryOf(character.Id);
        var matchingItems = inventory
            .Where(i => i.TemplateKey.Equals(matchingQuest.RequiredItemKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingItems.Count < matchingQuest.RequiredCount)
        {
            ctx.Reply($"You don't have enough {matchingQuest.RequiredItemKey}.");
            return true;
        }

        // Remove the required items, in storage as well as in the world - otherwise the turn-in
        // is undone by a restart and the quest can be handed in again with the same items.
        for (var i = 0; i < matchingQuest.RequiredCount; i++)
        {
            ctx.World.RemoveItem(matchingItems[i]);
            ctx.ItemSaveQueue?.EnqueueDelete(matchingItems[i].Id);
        }

        // Mark quest as completed
        var completedQuestState = ctx.World.GetQuestState(character.Id, matchingQuest.Key);
        if (completedQuestState is not null)
        {
            completedQuestState.Status = QuestStatus.Completed;
            completedQuestState.CompletedAt = DateTimeOffset.UtcNow;
            completedQuestState.TimesCompleted++;
            ctx.World.SetQuestState(character.Id, matchingQuest.Key, completedQuestState);

            // Persist the state change
            ctx.QuestSaveQueue?.Enqueue(new CharacterQuestSnapshot(
                character.Id, matchingQuest.Key, QuestStatus.Completed, completedQuestState.StartedAt,
                DateTimeOffset.UtcNow, completedQuestState.TimesCompleted));
        }

        // Narrate turn-in
        var turninReady = matchingQuest.Dialogue.TryGetValue("turninReady", out var turninText)
            ? turninText
            : $"Excellent work! You've completed {matchingQuest.Name}.";
        ctx.Reply(turninReady);

        AwardRewards(ctx, matchingQuest, character);

        // Handle level up
        while (DikuWeb.Domain.Characters.CharacterProgression.TryLevelUp(
            character.Level, character.Xp, character.Attributes, character.Path, character.Vitals) is var result && result != null)
        {
            character.Level = result.NewLevel;
            character.Attributes = result.NewAttributes;
            character.Vitals = result.NewVitals;
            ctx.Reply($"You advance to level {result.NewLevel}!", "levelup");
        }

        return true;
    }

    /// <summary>
    /// Pays out a completed quest: XP, gold, and reward items, all through the zone's
    /// difficulty dial.
    /// </summary>
    /// <remarks>
    /// Rewards used to be paid raw while combat awarded <c>mob.ResolvedXp</c>, so the same zone
    /// multipliers that made a mob worth triple left the quest beside it worth exactly its
    /// authored number. §7.5 calls multipliers "the reason the whole feature exists"; a reward
    /// path that ignores them makes quests the one thing the dial does not move.
    /// </remarks>
    private static void AwardRewards(CommandContext ctx, Quest quest, Character character)
    {
        var zone = ctx.World.FindZone(quest.ZoneKey);
        var world = zone is not null ? ctx.World.FindWorld(zone.WorldKey) : null;

        // A quest whose zone or world is missing is a content bug, not a reason to pay nothing.
        // Falling back to the authored numbers keeps the player whole while the builder fixes it;
        // silently awarding zero is what this did before, and it looked like a balance decision.
        var worldMultipliers = world?.Multipliers;
        var zoneMultipliers = zone?.Multipliers;

        var xp = Resolve(quest.RewardXp, MultiplierType.Xp);
        var gold = Resolve(quest.RewardGold, MultiplierType.Gold);

        character.Xp += xp;
        character.Gold += gold;

        if (xp > 0)
        {
            ctx.Reply($"You gain {xp} experience points.", "reward");
        }

        if (gold > 0)
        {
            ctx.Reply($"You gain {gold} gold.", "reward");
        }

        AwardRewardItems(ctx, quest, character, zone, world);
        return;

        int Resolve(int amount, MultiplierType type) =>
            worldMultipliers is null || zoneMultipliers is null
                ? amount
                : Multipliers.Resolve(amount, worldMultipliers, zoneMultipliers, type);
    }

    /// <summary>Hands over the quest's reward item, if it has one.</summary>
    private static void AwardRewardItems(
        CommandContext ctx,
        Quest quest,
        Character character,
        Zone? zone,
        DikuWeb.Domain.Worlds.World? world)
    {
        if (string.IsNullOrEmpty(quest.RewardItemKey) || quest.RewardItemCount <= 0)
        {
            return;
        }

        // Each failure below says so rather than returning quietly. A reward that does not
        // arrive and does not explain itself reads to a player as the quest being broken, and
        // to a builder as nothing at all.
        var itemTemplate = ctx.ItemTemplates?.Get(quest.RewardItemKey);
        if (itemTemplate is null)
        {
            ctx.Reply(
                $"({quest.RewardItemKey} was promised, but no such item exists any more. Tell a builder.)",
                "bad");
            return;
        }

        if (zone is null || world is null)
        {
            ctx.Reply(
                $"({itemTemplate.Name} was promised, but its zone is missing. Tell a builder.)",
                "bad");
            return;
        }

        var spawner = new DikuWeb.Engine.Spawning.ItemSpawner();

        // Spawned per copy, not once and cloned: each instance needs its own id, and the spawner
        // is the only place that stamps the questItem flag - which quest rewards are the most
        // likely items in the game to carry.
        for (var i = 0; i < quest.RewardItemCount; i++)
        {
            var instance = spawner.Spawn(itemTemplate, zone, world, character.RoomKey);

            ctx.World.AddItem(instance);
            ctx.World.PickUpItem(instance, character.Id);
            ctx.ItemSaveQueue?.Enqueue(instance);
        }

        // Once, after the loop. This sat inside it, so a count of three announced three times
        // that the player had received three.
        var received = quest.RewardItemCount == 1
            ? NarrationHelper.WithArticle(itemTemplate.Name)
            : $"{quest.RewardItemCount} x {itemTemplate.Name}";

        ctx.Reply($"You receive {received}.", "reward");
    }
}
