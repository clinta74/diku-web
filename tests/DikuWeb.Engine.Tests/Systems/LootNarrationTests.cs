using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// A kill says what it dropped, to whoever earned it.
/// </summary>
/// <remarks>
/// <para>
/// Loot appeared on the floor and nothing said so. The room listing redraws, so the item is
/// <em>there</em> to be found — but a player who killed something and walked on had no way to know
/// they had left anything behind, and a group had no way to know a drop had happened at all.
/// </para>
/// <para>
/// <b>The recipients are the kill credit, not the room.</b> Same people the experience went to, read
/// from the same helper — a party member who saw loot announced but was not paid for it, or the
/// reverse, is a disagreement that would take a long time to notice.
/// </para>
/// <para>
/// These are the first Engine tests that see a mob drop anything. The harness passed
/// <c>itemSpawner: null</c>, so <c>RollLoot</c> returned on its first line and the whole path was
/// unreachable from a test.
/// </para>
/// </remarks>
public sealed class LootNarrationTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>A loot entry that always drops.</summary>
    private static Dictionary<string, object> Always(string itemKey) =>
        new(StringComparer.Ordinal) { ["itemTemplateKey"] = itemKey, ["chance"] = 1.0 };

    /// <summary>A loot entry that never drops.</summary>
    private static Dictionary<string, object> Never(string itemKey) =>
        new(StringComparer.Ordinal) { ["itemTemplateKey"] = itemKey, ["chance"] = 0.0 };

    private static void DefineItem(WorldHarness harness, string key, string name) =>
        harness.ItemTemplates.Put(new ItemTemplate
        {
            Key = key,
            Name = name,
            Description = "Dropped.",
            Icon = "$",
        });

    /// <summary>
    /// A character standing over a mob that is one hit from death, with the given loot table.
    /// </summary>
    private static (WorldHarness Harness, PlayerActor Actor) Kill(
        params Dictionary<string, object>[] loot)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var actor = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 10);
        harness.AddMob("rat", West, health: 1, name: "large rat", loot: loot);

        return (harness, actor);
    }

    /// <summary>Forms a party, the way a player does.</summary>
    private static void Group(WorldHarness harness, PlayerActor leader, PlayerActor member)
    {
        harness.Execute(leader, $"group invite {member.Name}");
        harness.Execute(member, "group accept");

        Assert.True(
            harness.World.Parties.SameParty(leader.CharacterId, member.CharacterId),
            "the two should be grouped before the kill");
    }

    /// <summary>Fights until the mob is dead, then returns everything the actor was told.</summary>
    private static string FightToTheDeath(WorldHarness harness, PlayerActor actor)
    {
        harness.Drain(actor);
        harness.Execute(actor, "kill rat");

        // Long enough for a combat round to land the killing blow on a one-health mob.
        harness.Pump(32);

        return harness.DrainText(actor);
    }

    // -----------------------------------------------------------------------
    // What it says
    // -----------------------------------------------------------------------

    [Fact]
    public void A_drop_is_named()
    {
        var (harness, actor) = Kill(Always("fang"));
        DefineItem(harness, "fang", "a tarnished fang");

        Assert.Contains(
            "A large rat drops a tarnished fang.",
            FightToTheDeath(harness, actor),
            StringComparison.Ordinal);
    }

    /// <summary>An item authored without an article still reads as English.</summary>
    [Fact]
    public void A_bare_item_name_gets_an_article()
    {
        var (harness, actor) = Kill(Always("blade"));
        DefineItem(harness, "blade", "rusted blade");

        Assert.Contains(
            "A large rat drops a rusted blade.",
            FightToTheDeath(harness, actor),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Two_drops_are_joined_with_and()
    {
        var (harness, actor) = Kill(Always("fang"), Always("token"));
        DefineItem(harness, "fang", "a tarnished fang");
        DefineItem(harness, "token", "a carved token");

        Assert.Contains(
            "A large rat drops a tarnished fang and a carved token.",
            FightToTheDeath(harness, actor),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Three_drops_read_as_a_list()
    {
        var (harness, actor) = Kill(Always("fang"), Always("token"), Always("coin"));
        DefineItem(harness, "fang", "a tarnished fang");
        DefineItem(harness, "token", "a carved token");
        DefineItem(harness, "coin", "a bent coin");

        Assert.Contains(
            "A large rat drops a tarnished fang, a carved token and a bent coin.",
            FightToTheDeath(harness, actor),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Only what actually dropped. A table entry that missed its roll must not be announced — a
    /// player told about an item that is not there is worse off than one told nothing.
    /// </summary>
    [Fact]
    public void A_roll_that_missed_is_not_announced()
    {
        var (harness, actor) = Kill(Always("fang"), Never("token"));
        DefineItem(harness, "fang", "a tarnished fang");
        DefineItem(harness, "token", "a carved token");

        var said = FightToTheDeath(harness, actor);

        Assert.Contains("drops a tarnished fang.", said, StringComparison.Ordinal);
        Assert.DoesNotContain("carved token", said, StringComparison.Ordinal);
    }

    /// <summary>A mob with nothing to drop says nothing, rather than dropping nothing out loud.</summary>
    [Fact]
    public void A_mob_that_drops_nothing_says_nothing()
    {
        var (harness, actor) = Kill(Never("fang"));
        DefineItem(harness, "fang", "a tarnished fang");

        Assert.DoesNotContain("drops", FightToTheDeath(harness, actor), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mob_with_no_loot_table_says_nothing()
    {
        var (harness, actor) = Kill();

        Assert.DoesNotContain("drops", FightToTheDeath(harness, actor), StringComparison.Ordinal);
    }

    /// <summary>
    /// The item really is on the floor. Without this the narration test would pass against a line
    /// that announces a drop which never happened.
    /// </summary>
    [Fact]
    public void What_was_announced_is_actually_in_the_room()
    {
        var (harness, actor) = Kill(Always("fang"));
        DefineItem(harness, "fang", "a tarnished fang");

        FightToTheDeath(harness, actor);

        Assert.Contains(
            harness.World.ItemsIn(West),
            i => i.DisplayName == "a tarnished fang");
    }

    // -----------------------------------------------------------------------
    // Who hears it
    // -----------------------------------------------------------------------

    /// <summary>
    /// A party member standing where it died hears it — the same rule that pays them.
    /// </summary>
    [Fact]
    public void A_party_member_present_is_told_as_well()
    {
        var (harness, killer) = Kill(Always("fang"));
        DefineItem(harness, "fang", "a tarnished fang");

        var friend = harness.AddPlayer("Wen", West, path: CharacterPath.Hallow, level: 10);
        Group(harness, killer, friend);

        harness.Drain(friend);
        FightToTheDeath(harness, killer);

        Assert.Contains(
            "drops a tarnished fang",
            harness.DrainText(friend),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A bystander is not.</b> The room already redraws its contents, so this line is the answer
    /// to "did I get anything for that" rather than news about the floor.
    /// </summary>
    [Fact]
    public void A_bystander_in_the_room_is_not_told()
    {
        var (harness, killer) = Kill(Always("fang"));
        DefineItem(harness, "fang", "a tarnished fang");

        var onlooker = harness.AddPlayer("Kaeda", West, path: CharacterPath.Shade, level: 10);

        harness.Drain(onlooker);
        FightToTheDeath(harness, killer);

        var seen = harness.DrainText(onlooker);

        // They do learn the rat died - that is a room event and always was.
        Assert.Contains("A large rat falls.", seen, StringComparison.Ordinal);
        Assert.DoesNotContain("drops", seen, StringComparison.Ordinal);
    }

    /// <summary>
    /// A party member who is elsewhere hears nothing, which is the same rule that gives them no
    /// experience: present means standing where it died.
    /// </summary>
    [Fact]
    public void A_party_member_in_another_room_is_not_told()
    {
        var (harness, killer) = Kill(Always("fang"));
        DefineItem(harness, "fang", "a tarnished fang");

        // Grouped where they can see each other, then sent away - a party forms by invitation and
        // acceptance, which needs both of them present.
        var away = harness.AddPlayer("Wen", West, level: 10);
        Group(harness, killer, away);
        harness.Execute(away, "east");
        Assert.NotEqual(West, away.Character.RoomKey);

        harness.Drain(away);
        FightToTheDeath(harness, killer);

        Assert.DoesNotContain("drops", harness.DrainText(away), StringComparison.Ordinal);
    }

    /// <summary>
    /// The loot line comes after the death line, because that is the order the two things happen in
    /// and reading them the other way round is a small confusion on every kill.
    /// </summary>
    [Fact]
    public void The_drop_is_announced_after_the_death()
    {
        var (harness, actor) = Kill(Always("fang"));
        DefineItem(harness, "fang", "a tarnished fang");

        var said = FightToTheDeath(harness, actor);

        Assert.True(
            said.IndexOf("falls.", StringComparison.Ordinal)
                < said.IndexOf("drops", StringComparison.Ordinal),
            said);
    }
}
