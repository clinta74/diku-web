using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// A drop belongs to whoever earned it, for a couple of minutes.
/// </summary>
/// <remarks>
/// <para>
/// Loot lands on the floor rather than in a corpse, and <c>get</c> asked nothing about entitlement —
/// so somebody who stood and watched the fight was exactly as entitled as the party that fought it,
/// and typing faster was the whole of the rule.
/// </para>
/// <para>
/// <b>The set that may take it is the set that was paid</b>, read from <c>KillCredit</c> rather
/// than recomputed: a party member told about a drop they are then refused would be the same
/// disagreement as one paid for a kill they were not present at.
/// </para>
/// <para>
/// <b>The expiry is not a balance knob, it is a leak fix.</b> Nothing sweeps dropped items, so a
/// claim that never lapsed would leave every unwanted drop on the floor as furniture nobody could
/// ever pick up. <see cref="Expiry_frees_it_for_everybody"/> is the test that keeps that true.
/// </para>
/// </remarks>
public sealed class LootClaimTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static Dictionary<string, object> Always(string itemKey) =>
        new(StringComparer.Ordinal) { ["itemTemplateKey"] = itemKey, ["chance"] = 1.0 };

    /// <summary>A killer standing over a one-hit rat that always drops a fang.</summary>
    private static (WorldHarness Harness, PlayerActor Killer) Kill()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.ItemTemplates.Put(new ItemTemplate
        {
            Key = "fang",
            Name = "a tarnished fang",
            Description = "Dropped.",
            Icon = "$",
        });

        var killer = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 10);
        harness.AddMob("rat", West, health: 1, name: "large rat", loot: [Always("fang")]);

        return (harness, killer);
    }

    private static void Group(WorldHarness harness, PlayerActor leader, PlayerActor member)
    {
        harness.Execute(leader, $"group invite {member.Name}");
        harness.Execute(member, "group accept");

        Assert.True(
            harness.World.Parties.SameParty(leader.CharacterId, member.CharacterId),
            "the two should be grouped before the kill");
    }

    private static void FightToTheDeath(WorldHarness harness, PlayerActor actor)
    {
        harness.Execute(actor, "kill rat");
        harness.Pump(32);

        Assert.Contains(
            harness.World.ItemsIn(West),
            i => i.DisplayName == "a tarnished fang");
    }

    /// <summary>What this character was told when they reached for the fang.</summary>
    private static string TryToTake(WorldHarness harness, PlayerActor actor)
    {
        harness.Drain(actor);
        harness.Execute(actor, "get fang");

        return harness.DrainText(actor);
    }

    private static bool Holds(WorldHarness harness, PlayerActor actor) =>
        harness.World.InventoryOf(actor.CharacterId)
            .Any(i => i.DisplayName == "a tarnished fang");

    // -----------------------------------------------------------------------
    // Who may take it
    // -----------------------------------------------------------------------

    [Fact]
    public void The_killer_may_take_their_own_drop()
    {
        var (harness, killer) = Kill();
        FightToTheDeath(harness, killer);

        Assert.Contains(
            "You take the tarnished fang.", TryToTake(harness, killer), StringComparison.Ordinal);
        Assert.True(Holds(harness, killer));
    }

    /// <summary>
    /// The whole point. A bystander is refused, and told enough to act on: whose it is, and that
    /// waiting is what fixes it. A bare "you cannot take that" would be indistinguishable from a
    /// bug.
    /// </summary>
    [Fact]
    public void A_bystander_is_refused_and_told_whose_it_is()
    {
        var (harness, killer) = Kill();
        var onlooker = harness.AddPlayer("Kaeda", West, path: CharacterPath.Temper, level: 10);

        FightToTheDeath(harness, killer);

        var said = TryToTake(harness, onlooker);

        Assert.Contains("The tarnished fang belongs to Bram", said, StringComparison.Ordinal);
        Assert.Contains("for another", said, StringComparison.Ordinal);
        Assert.False(Holds(harness, onlooker));

        // And it is still there for the person it belongs to.
        Assert.Contains(harness.World.ItemsIn(West), i => i.DisplayName == "a tarnished fang");
    }

    /// <summary>
    /// A group shares the claim, which is the requirement this exists to serve: any member may
    /// take any of it, and nobody has to be the one who landed the blow.
    /// </summary>
    [Fact]
    public void A_party_member_present_may_take_it()
    {
        var (harness, killer) = Kill();
        var friend = harness.AddPlayer("Wen", West, path: CharacterPath.Hallow, level: 10);
        Group(harness, killer, friend);

        FightToTheDeath(harness, killer);

        Assert.Contains(
            "You take the tarnished fang.", TryToTake(harness, friend), StringComparison.Ordinal);
        Assert.True(Holds(harness, friend));
    }

    /// <summary>
    /// Present means standing where it died — the same rule that decides the experience split. A
    /// member who was in the next room is not owed a share of either.
    /// </summary>
    [Fact]
    public void A_party_member_who_was_elsewhere_is_refused()
    {
        var (harness, killer) = Kill();
        var away = harness.AddPlayer("Wen", West, level: 10);
        Group(harness, killer, away);

        harness.Execute(away, "east");
        Assert.NotEqual(West, away.Character.RoomKey);

        FightToTheDeath(harness, killer);

        // Back after the fact, hand out.
        harness.Execute(away, "west");
        Assert.Equal(West, away.Character.RoomKey);

        Assert.Contains("belongs to Bram", TryToTake(harness, away), StringComparison.Ordinal);
        Assert.False(Holds(harness, away));
    }

    // -----------------------------------------------------------------------
    // When it stops mattering
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>The claim must lapse.</b> Nothing in the game sweeps dropped items, so a permanent claim
    /// would turn every drop a party walked away from into scenery — an item visible in the room
    /// that no living character could ever pick up.
    /// </summary>
    [Fact]
    public void Expiry_frees_it_for_everybody()
    {
        var (harness, killer) = Kill();
        var onlooker = harness.AddPlayer("Kaeda", West, path: CharacterPath.Temper, level: 10);

        FightToTheDeath(harness, killer);
        Assert.Contains(
            "belongs to Bram", TryToTake(harness, onlooker), StringComparison.Ordinal);

        harness.Clock.Advance(LootClaim.Window + TimeSpan.FromSeconds(1));

        Assert.Contains(
            "You take the tarnished fang.", TryToTake(harness, onlooker), StringComparison.Ordinal);
        Assert.True(Holds(harness, onlooker));
    }

    /// <summary>
    /// Picking it up ends the claim. Left on the instance it would come back the moment the item
    /// was put down, so a party's share could be banked for good by taking it and dropping it —
    /// and the member it was dropped for would be the one refused.
    /// </summary>
    [Fact]
    public void Taking_it_and_dropping_it_leaves_it_free()
    {
        var (harness, killer) = Kill();
        var onlooker = harness.AddPlayer("Kaeda", West, path: CharacterPath.Temper, level: 10);

        FightToTheDeath(harness, killer);

        harness.Execute(killer, "get fang");
        harness.Execute(killer, "drop fang");
        Assert.Contains(harness.World.ItemsIn(West), i => i.DisplayName == "a tarnished fang");

        Assert.Contains(
            "You take the tarnished fang.", TryToTake(harness, onlooker), StringComparison.Ordinal);
        Assert.True(Holds(harness, onlooker));
    }

    /// <summary>
    /// Only mob drops are claimed. An item lying in a room because a builder put it there, or
    /// because somebody dropped it, belongs to whoever reaches it — which is how it has always
    /// worked and is not what was broken.
    /// </summary>
    [Fact]
    public void An_item_that_was_never_loot_is_free_to_take()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.ItemTemplates.Put(new ItemTemplate
        {
            Key = "fang",
            Name = "a tarnished fang",
            Description = "Just lying there.",
            Icon = "$",
        });

        var finder = harness.AddPlayer("Kaeda", West, path: CharacterPath.Temper, level: 10);
        harness.World.AddItem(new ItemInstance
        {
            TemplateKey = "fang",
            TemplateName = "a tarnished fang",
            RoomKey = West.ToString(),
        });

        Assert.Contains(
            "You take the tarnished fang.", TryToTake(harness, finder), StringComparison.Ordinal);
        Assert.True(Holds(harness, finder));
    }
}
