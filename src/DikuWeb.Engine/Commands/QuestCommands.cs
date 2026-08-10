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

        // "quest" goes in FIRST, and the order is load-bearing. A verb matches on any prefix of
        // its name, and Find takes the first definition that matches - so with "quests" ahead of
        // it, typing `quest` matched *quests* ("quests".StartsWith("quest")) and QuestDetail had
        // no reachable input at all. It was dead from the day it was written.
        //
        // What keeps the other prefix pairs safe is that the longer verb demands more characters
        // than the shorter one has: `whois` needs 5, so "who" can never reach it, and `stats`
        // needs 5, so "stat" cannot. "quests" asking for only 3 is what broke the symmetry.
        commands.Add(new CommandDefinition(
            "quest", 3, "quest [name] - your journal, or one quest in detail", QuestDetail));

        commands.Add(new CommandDefinition(
            "quests", 3, "quests - list your active quests", Quests));

        commands.Add(new CommandDefinition(
            "abandon", 3, "abandon <name> - give up an active quest", Abandon));
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

            // A quest whose content has been deleted is dormant: it stops being offered, and
            // anyone already holding it is told so rather than left hunting for an item that no
            // longer exists (PLAN.md §7.4).
            if (IsDormant(ctx, quest))
            {
                if (questState?.Status == QuestStatus.Active)
                {
                    narrations.Add(
                        $"About {quest.Name} — that business is closed for now. Hold on to what you have.");
                }

                continue;
            }

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
            else if (questState?.Status == QuestStatus.Completed && quest.IsRepeatable
                && ChainStillRunning(ctx, character.Id, quest.Key) is { } blocking)
            {
                // Repeatable, finished, but something further down the chain is still open. Taking
                // it again now would reset a step whose consequences are still in play.
                narrations.Add(
                    $"About {quest.Name} — finish {blocking.Name} first, or give it up.");
            }
            else if (questState?.Status == QuestStatus.Completed && quest.IsRepeatable
                && !UpstreamHasRunAgain(ctx, character.Id, quest, questState))
            {
                // Repeatable and clear below, but the step *above* has not been run again - so
                // this is a player re-entering the chain in the middle. Left open, the old man
                // would hand out the second errand to somebody with no glass and no way to get
                // one, because taking the first errand is then blocked by this one being active.
                // Recoverable by abandoning, but a chain should be re-entered at its head.
                narrations.Add($"About {quest.Name} — that comes later. First things first.");
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

    /// <summary>
    /// Whether this quest points at content that no longer exists.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, and deliberately not a <c>QuestStatus.Unavailable</c>. The
    /// state is entirely a function of what content exists right now, so deriving it means a
    /// builder who deletes a mob by mistake and puts it back has every stranded quest working
    /// again with no repair pass - and a stored status would need one, plus a migration, plus a
    /// rule for when to flip it back.
    ///
    /// What §7.4 actually requires is that <c>character_quests</c> rows survive and that the
    /// giver stops offering it. Both are true here, and no player row is ever written.
    ///
    /// Keys are not foreign keys on purpose (§7.4: a builder wires a quest before creating what
    /// it references), so "missing" is normal and temporary rather than corrupt.
    /// </remarks>
    private static bool IsDormant(CommandContext ctx, Quest quest)
    {
        if (ctx.MobTemplates is null)
        {
            return false;
        }

        if (ctx.MobTemplates.Get(quest.GiverMobKey) is null ||
            ctx.MobTemplates.Get(quest.TurninMobKey) is null)
        {
            return true;
        }

        // The required item only makes a quest dormant if it is named at all - a quest with no
        // fetch step is legitimate, which is why RequiredItemKey is nullable.
        return !string.IsNullOrEmpty(quest.RequiredItemKey) &&
               ctx.ItemTemplates?.Get(quest.RequiredItemKey) is null;
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

                // A dormant quest is still yours and still in the journal - it just cannot be
                // progressed until the content comes back. Saying so beats leaving a player
                // hunting for an item that no longer exists (PLAN.md §7.4).
                var dormant = questDef is not null && IsDormant(ctx, questDef);
                var suffix = dormant ? " (unavailable — content missing)" : string.Empty;

                ctx.Reply($"  {questDef?.Name ?? quest.QuestKey}: {summary}{suffix}");
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

    /// <summary>
    /// Gives up an active quest, returning it to never-started so the giver will offer it again.
    /// </summary>
    /// <remarks>
    /// A chain is the reason this exists. Prerequisites mean an abandoned leg blocks every quest
    /// behind it, and before this there was no way out of one: the journal listed it Active for
    /// ever and the giver answered with its in-progress line. That is a soft-lock made of
    /// dialogue rather than of code, which is the hardest kind to notice.
    ///
    /// It removes the state rather than marking it, because §6 spells "not started" as the
    /// absence of a row - so no new status, no migration, and nothing else has to learn a third
    /// state. The one exception is a repeatable quest already finished at least once: deleting
    /// that row would erase the history in <c>TimesCompleted</c>, so it reverts to Completed and
    /// keeps the count.
    ///
    /// Items are deliberately left alone. Taking them back would be destroying player property on
    /// a verb typed by mistake, and worse, the item may have come from an earlier leg that is no
    /// longer repeatable - which would make the chain permanently unfinishable rather than merely
    /// abandoned. A held item is not dead weight either: <c>drop</c> carries no quest-item guard,
    /// only <c>destroy</c> and <c>sell</c> do, so it can be put down and picked back up if the
    /// quest is taken again.
    /// </remarks>
    private static void Abandon(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Abandon which quest?");
            return;
        }

        var character = ctx.Actor.Character;

        var questState = FindQuestByName(ctx, character.Id, ctx.Argument);

        if (questState is null)
        {
            ctx.Reply("You don't have that quest.");
            return;
        }

        var name = ctx.Quests?.Get(questState.QuestKey)?.Name ?? questState.QuestKey;

        if (questState.Status != QuestStatus.Active)
        {
            // Named rather than generic: "you don't have that quest" would be a lie about
            // something sitting in the journal two lines above.
            ctx.Reply($"You have already finished {name}.");
            return;
        }

        if (questState.TimesCompleted > 0)
        {
            questState.Status = QuestStatus.Completed;
            ctx.World.SetQuestState(character.Id, questState.QuestKey, questState);
            ctx.QuestSaveQueue?.Enqueue(new CharacterQuestSnapshot(
                character.Id,
                questState.QuestKey,
                QuestStatus.Completed,
                questState.StartedAt,
                questState.CompletedAt,
                questState.TimesCompleted));
        }
        else
        {
            ctx.World.RemoveQuestState(character.Id, questState.QuestKey);
            ctx.QuestSaveQueue?.EnqueueDelete(character.Id, questState.QuestKey);
        }

        ctx.Reply($"You give up on {name}. You can ask for it again.");
    }

    /// <summary>
    /// Whether every prerequisite has been finished more times than this quest has, which is what
    /// makes a repeat a fresh run through the chain rather than a re-entry into the middle of it.
    /// </summary>
    /// <remarks>
    /// <c>TimesCompleted</c> already counts the runs, so the comparison needs no new state: after
    /// one full pass both legs sit at 1 and the second is not offered again; run the first leg
    /// once more and it is at 2, which is what unlocks the second.
    ///
    /// A quest with no prerequisites is the head of its chain and always passes — otherwise
    /// nothing would ever be repeatable at all.
    /// </remarks>
    private static bool UpstreamHasRunAgain(
        CommandContext ctx, Guid characterId, Quest quest, CharacterQuest state)
    {
        foreach (var prerequisiteKey in quest.PrerequisiteQuestKeys)
        {
            var prerequisite = ctx.World.GetQuestState(characterId, prerequisiteKey);

            if (prerequisite is null || prerequisite.TimesCompleted <= state.TimesCompleted)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The quest still open somewhere downstream of <paramref name="questKey"/>, or null if the
    /// chain below it is clear.
    /// </summary>
    /// <remarks>
    /// <b>A repeatable quest is repeatable once the chain it starts is done, not once its own leg
    /// is.</b> Repeatability was a property of the single quest, so a player who had finished
    /// <c>A Fresh Drink</c> and was carrying the glass could take the errand again mid-chain: the
    /// first leg reset to Active while the second stayed Active behind it, and the journal then
    /// described a state the story cannot be in — the beer not yet delivered, the glass already
    /// in hand.
    ///
    /// Downstream is transitive, because a chain is not only two long. It is walked rather than
    /// stored, for the reason dormancy is derived (§7.4): prerequisites are edited live, and a
    /// cached answer would be wrong the moment a builder inserted a step.
    ///
    /// Only <em>Active</em> blocks. A completed step downstream is exactly the case this is meant
    /// to allow, and the visited set is what keeps an authored cycle — which the storyline panel
    /// reports but does not prevent — from walking forever.
    /// </remarks>
    private static Quest? ChainStillRunning(CommandContext ctx, Guid characterId, string questKey)
    {
        if (ctx.Quests is null)
        {
            return null;
        }

        var all = ctx.Quests.All;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { questKey };
        var frontier = new Queue<string>();
        frontier.Enqueue(questKey);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            foreach (var candidate in all.Values)
            {
                if (!candidate.PrerequisiteQuestKeys.Contains(current, StringComparer.OrdinalIgnoreCase)
                    || !visited.Add(candidate.Key))
                {
                    continue;
                }

                if (ctx.World.GetQuestState(characterId, candidate.Key)?.Status == QuestStatus.Active)
                {
                    return candidate;
                }

                frontier.Enqueue(candidate.Key);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds one of a character's quests by a fragment of its display name.
    /// </summary>
    /// <remarks>
    /// Shared by <c>quest</c> and <c>abandon</c> so the two agree about what a player means -
    /// two copies of a fuzzy match is two chances to disagree about which quest was named.
    /// </remarks>
    private static CharacterQuest? FindQuestByName(CommandContext ctx, Guid characterId, string name) =>
        ctx.World.QuestsFor(characterId).FirstOrDefault(q =>
            ctx.Quests?.Get(q.QuestKey)?.Name.Contains(name, StringComparison.OrdinalIgnoreCase) == true);

    private static void QuestDetail(CommandContext ctx)
    {
        // Bare `quest` shows the journal rather than asking "Which quest?". The two verbs are one
        // family and the argument is what distinguishes them, so a player who types the shorter
        // word gets the more useful answer instead of a question. It also means every
        // abbreviation from "que" up stays useful now that this definition is matched first.
        if (!ctx.HasArgument)
        {
            Quests(ctx);
            return;
        }

        var character = ctx.Actor.Character;
        var questState = FindQuestByName(ctx, character.Id, ctx.Argument);

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
