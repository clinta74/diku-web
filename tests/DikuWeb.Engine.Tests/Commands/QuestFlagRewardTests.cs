using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// A quest granting a capability, and that capability opening a gate (PLAN.md §4.15).
/// </summary>
/// <remarks>
/// This is the whole attunement loop in miniature: finish the chain, hold the flag, walk through
/// the door that refused you a moment ago. It is tested end to end rather than as two halves
/// because the halves are useless apart - a flag nothing grants is an unopenable gate, and a flag
/// no gate asks for is a string on a character sheet.
/// </remarks>
public sealed class QuestFlagRewardTests
{
    private const string Flag = "attuned.grask";

    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    /// <summary>A giver standing in the east room, holding a quest that grants the flag.</summary>
    private static (WorldHarness Harness, DikuWeb.Engine.World.PlayerActor Player) Ready(
        bool repeatable = false)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", East);
        harness.AddMob("keeper", East, name: "keeper");
        harness.DefineQuest(
            "attune-grask",
            giverMobKey: "keeper",
            requiredItemKey: "token",
            repeatable: repeatable,
            rewardFlagKey: Flag);

        var token = harness.DefineItem("token", "gate token", slot: null);
        harness.GiveItem(kael, token);

        harness.Execute(kael, "talk keeper");
        harness.Drain(kael);

        return (harness, kael);
    }

    [Fact]
    public void Finishing_the_quest_grants_the_flag()
    {
        var (harness, kael) = Ready();

        harness.Execute(kael, "give token keeper");

        Assert.True(kael.Character.HasFlag(Flag));
    }

    [Fact]
    public void Not_finishing_it_grants_nothing()
    {
        var (harness, kael) = Ready();

        Assert.False(kael.Character.HasFlag(Flag));
        harness.Drain(kael);
    }

    [Fact]
    public void The_grant_is_narrated()
    {
        var (harness, kael) = Ready();

        harness.Execute(kael, "give token keeper");

        Assert.Contains("not any more", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeatable_quest_does_not_grant_it_twice()
    {
        // A capability is held or it is not; a second copy would be a set that had stopped being
        // one, and Character.HasFlag would start depending on how many times you ran the chain.
        var (harness, kael) = Ready(repeatable: true);

        harness.Execute(kael, "give token keeper");

        var token = harness.DefineItem("token", "gate token", slot: null);
        harness.GiveItem(kael, token);
        harness.Execute(kael, "talk keeper");
        harness.Execute(kael, "give token keeper");

        Assert.Single(kael.Character.Flags, f => string.Equals(f, Flag, StringComparison.Ordinal));
    }

    /// <summary>
    /// The same grant, for a quest authored the way the game actually authors one.
    /// </summary>
    /// <remarks>
    /// <b>Every other case in this file calls <c>DefineQuest</c>, which puts a <c>Quest</c> straight
    /// into the cache.</b> That is why they all passed while the feature was broken: the domain
    /// logic was never the problem. A quest reaches a running server through
    /// <see cref="WorldMutationApplier"/> — one `UpsertQuest` per entity, from the builder and from
    /// every import — and that path dropped `RewardFlagKey` on the floor while writing the database
    /// row correctly. So the flag was granted after a restart and not before it, which is the
    /// hardest shape of bug to see from a test that never restarts.
    ///
    /// The gates are the only progression lock in the game, so this is the one field where "live
    /// immediate" failing quietly costs a player the rest of the world.
    /// </remarks>
    [Fact]
    public void A_quest_authored_through_the_applier_still_grants_its_flag()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var kael = harness.AddPlayer("Kael", East);
        harness.AddMob("keeper", East, name: "keeper");

        harness.Mutate(new UpsertQuest(
            "attune-grask",
            "test.zone",
            "Attune to Grask",
            "Bring the keeper a token.",
            string.Empty,
            GiverMobKey: "keeper",
            TurninMobKey: "keeper",
            RequiredItemKey: "token",
            RequiredCount: 1,
            RewardXp: 0,
            RewardGold: 0,
            RewardItemKey: null,
            RewardItemCount: 0,
            RewardFlagKey: Flag,
            PrerequisiteQuestKeys: [],
            IsRepeatable: false,
            AutoStart: false,
            Dialogue: [],
            SortOrder: 0));

        var token = harness.DefineItem("token", "gate token", slot: null);
        harness.GiveItem(kael, token);

        harness.Execute(kael, "talk keeper");
        harness.Execute(kael, "give token keeper");

        Assert.True(kael.Character.HasFlag(Flag));
    }

    [Fact]
    public void The_flag_the_quest_grants_opens_the_gate_it_was_for()
    {
        // The point of all of it: the exit names the capability, the quest grants the capability,
        // and neither of them has ever heard of the other.
        var (harness, kael) = Ready();
        harness.Mutate(new SetExit(East, Direction.West, West, Flag, null, "The gate does not know you."));

        harness.Execute(kael, "west");
        Assert.Equal(East, kael.RoomKey);

        harness.Execute(kael, "give token keeper");
        harness.Execute(kael, "west");

        Assert.Equal(West, kael.RoomKey);
    }
}
