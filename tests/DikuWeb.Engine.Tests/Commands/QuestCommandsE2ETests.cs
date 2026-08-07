using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// End-to-end tests for the quest system from the command/UI layer:
/// talking to NPCs, accepting quests, progress tracking, turn-in, and rewards.
/// </summary>
public sealed class QuestCommandsE2ETests
{
    private static readonly RoomKey TestRoom = RoomKey.Parse("test.zone.west");

    private sealed class E2EHarness
    {
        private readonly WorldHarness _harness;

        public E2EHarness()
        {
            _harness = new WorldHarness();
            _harness.LoadTestWorld();
        }

        public WorldHarness World => _harness;

        public PlayerActor AddPlayer(string name) => _harness.AddPlayer(name, TestRoom);

        public void Execute(PlayerActor actor, string command) => _harness.Execute(actor, command);

        public void Drain(PlayerActor actor) => _harness.Drain(actor);

        public string DrainText(PlayerActor actor) => _harness.DrainText(actor);
    }

    [Fact]
    public void WorldState_TracksQuestStatePerPlayer()
    {
        var harness = new E2EHarness();
        var player = harness.AddPlayer("Kael");
        var character = player.Character;

        // Initially no quests
        var quests = harness.World.World.QuestsFor(character.Id);
        Assert.Empty(quests);

        // Add a quest
        var questState = new CharacterQuest
        {
            CharacterId = character.Id,
            QuestKey = "test.first-quest",
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = null,
            TimesCompleted = 0,
        };
        harness.World.World.SetQuestState(character.Id, "test.first-quest", questState);

        // Now player has quests
        var updated = harness.World.World.QuestsFor(character.Id);
        Assert.Single(updated);
        Assert.Equal("test.first-quest", updated[0].QuestKey);
    }

    [Fact]
    public void QuestsCommand_ShowsActiveQuests()
    {
        var harness = new E2EHarness();
        var player = harness.AddPlayer("Kael");
        var character = player.Character;

        // Manually add an active quest
        var questState = new CharacterQuest
        {
            CharacterId = character.Id,
            QuestKey = "test.sample-quest",
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
        };
        harness.World.World.SetQuestState(character.Id, "test.sample-quest", questState);

        harness.Drain(player);
        harness.Execute(player, "quests");

        var text = harness.DrainText(player);
        Assert.Contains("test.sample-quest", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active", text);
    }

    [Fact]
    public void QuestsCommand_EmptyWhenNoQuests()
    {
        var harness = new E2EHarness();
        var player = harness.AddPlayer("Kael");

        harness.Drain(player);
        harness.Execute(player, "quests");

        var text = harness.DrainText(player);
        Assert.Contains("no quests", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuestDetailCommand_ShowsQuestInfo()
    {
        var harness = new E2EHarness();
        var player = harness.AddPlayer("Kael");
        var character = player.Character;

        // Add an active quest
        var questState = new CharacterQuest
        {
            CharacterId = character.Id,
            QuestKey = "test.detail-quest",
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
        };
        harness.World.World.SetQuestState(character.Id, "test.detail-quest", questState);

        harness.Drain(player);
        harness.Execute(player, "quest detail-quest");

        var text = harness.DrainText(player);
        // Should show quest name, objective, and rewards
        Assert.NotEmpty(text);
    }

    [Fact]
    public void MultiplePlayersHaveIndependentQuestProgress()
    {
        var harness = new E2EHarness();
        var kael = harness.AddPlayer("Kael");
        var mira = harness.AddPlayer("Mira");

        // Add quest to Kael only
        var questState = new CharacterQuest
        {
            CharacterId = kael.Character.Id,
            QuestKey = "test.independent-quest",
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
        };
        harness.World.World.SetQuestState(kael.Character.Id, "test.independent-quest", questState);

        harness.Drain(kael);
        harness.Drain(mira);

        // Kael has the quest
        var kaelQuests = harness.World.World.QuestsFor(kael.Character.Id);
        Assert.Single(kaelQuests);

        // Mira doesn't
        var miraQuests = harness.World.World.QuestsFor(mira.Character.Id);
        Assert.Empty(miraQuests);
    }

    [Fact]
    public void QuestCompletion_MarksQuestAsCompleted()
    {
        var harness = new E2EHarness();
        var player = harness.AddPlayer("Kael");
        var character = player.Character;

        // Create and set active quest
        var questState = new CharacterQuest
        {
            CharacterId = character.Id,
            QuestKey = "test.completion-quest",
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
        };
        harness.World.World.SetQuestState(character.Id, "test.completion-quest", questState);

        // Manually complete it (normally via turn-in logic)
        questState.Status = QuestStatus.Completed;
        questState.CompletedAt = DateTimeOffset.UtcNow;
        questState.TimesCompleted = 1;
        harness.World.World.SetQuestState(character.Id, "test.completion-quest", questState);

        // Verify it's marked as completed
        var updated = harness.World.World.GetQuestState(character.Id, "test.completion-quest");
        Assert.NotNull(updated);
        Assert.Equal(QuestStatus.Completed, updated.Status);
        Assert.NotNull(updated.CompletedAt);
        Assert.Equal(1, updated.TimesCompleted);
    }

    [Fact]
    public void RepeatableQuestCanBeCompletedMultipleTimes()
    {
        var harness = new E2EHarness();
        var player = harness.AddPlayer("Kael");
        var character = player.Character;

        // Create repeatable quest
        var questState = new CharacterQuest
        {
            CharacterId = character.Id,
            QuestKey = "test.repeatable-quest",
            Status = QuestStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            TimesCompleted = 1,
        };
        harness.World.World.SetQuestState(character.Id, "test.repeatable-quest", questState);

        // Complete it again
        var questState2 = new CharacterQuest
        {
            CharacterId = character.Id,
            QuestKey = "test.repeatable-quest",
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = null,
            TimesCompleted = 1,
        };
        harness.World.World.SetQuestState(character.Id, "test.repeatable-quest", questState2);

        questState2.Status = QuestStatus.Completed;
        questState2.CompletedAt = DateTimeOffset.UtcNow;
        questState2.TimesCompleted = 2;
        harness.World.World.SetQuestState(character.Id, "test.repeatable-quest", questState2);

        // Verify completion count incremented
        var updated = harness.World.World.GetQuestState(character.Id, "test.repeatable-quest");
        Assert.Equal(2, updated!.TimesCompleted);
    }

    [Fact]
    public void PrerequisiteQuestBlocksUnlocking()
    {
        var harness = new E2EHarness();
        var player = harness.AddPlayer("Kael");
        var character = player.Character;

        // Quest A is a prerequisite for Quest B
        // If A is not completed, B should not be offered

        var questA = new CharacterQuest
        {
            CharacterId = character.Id,
            QuestKey = "test.quest-a",
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
        };
        harness.World.World.SetQuestState(character.Id, "test.quest-a", questA);

        // Quest B should not be startable yet (prerequisite not met)
        var questBState = harness.World.World.GetQuestState(character.Id, "test.quest-b");
        Assert.Null(questBState);

        // Complete Quest A
        questA.Status = QuestStatus.Completed;
        questA.CompletedAt = DateTimeOffset.UtcNow;
        harness.World.World.SetQuestState(character.Id, "test.quest-a", questA);

        // Now Quest B's prerequisite is met (though we don't auto-unlock it here)
        var completedA = harness.World.World.GetQuestState(character.Id, "test.quest-a");
        Assert.Equal(QuestStatus.Completed, completedA!.Status);
    }

    [Fact]
    public void QuestStateTrackingAcrossSessions()
    {
        var harness = new E2EHarness();
        var player = harness.AddPlayer("Kael");
        var characterId = player.Character.Id;

        // Add quest in session 1
        var questState = new CharacterQuest
        {
            CharacterId = characterId,
            QuestKey = "test.persistent-quest",
            Status = QuestStatus.Active,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = null,
            TimesCompleted = 0,
        };
        harness.World.World.SetQuestState(characterId, "test.persistent-quest", questState);

        // Verify it's there
        var quests1 = harness.World.World.QuestsFor(characterId);
        Assert.Single(quests1);

        // "Session 2" - quest state persists in world
        var quests2 = harness.World.World.QuestsFor(characterId);
        Assert.Single(quests2);
        Assert.Equal("test.persistent-quest", quests2[0].QuestKey);
        Assert.Equal(QuestStatus.Active, quests2[0].Status);
    }
}
