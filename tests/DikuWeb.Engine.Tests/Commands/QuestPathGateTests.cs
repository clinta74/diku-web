using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// A quest is offered to the Paths it is for (PLAN.md §5.1).
/// </summary>
/// <remarks>
/// <para>
/// Reported from play. The four epic chains have one giver and Path-locked rewards, so
/// <c>talk vesh</c> handed every character all four — and a Temper who finished the Adept chain
/// received a stormrod they could not wield and, being lore and no-drop, could not drop, sell or
/// destroy. The journal read:
/// </para>
/// <code>
/// Active:
///   The Stormrod, unproven      (Adept)
///   The Hearthcenser, unproven  (Hallow)
///   The Quiet Knife, unproven   (Temper)
///   The Oathmaul, unproven      (Warden)
/// </code>
/// <para>
/// <b>Empty means anyone</b>, so the fifteen unrestricted quests behave exactly as before — which
/// is the property most of this file is spent on, because a gate that quietly narrowed every quest
/// in the game would be a worse bug than the one it fixed.
/// </para>
/// </remarks>
public sealed class QuestPathGateTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static UpsertQuest Quest(
        string key,
        string name,
        IEnumerable<CharacterPath>? paths = null,
        string? requiredItem = null) =>
        new(key, "test.zone", name, $"Do {name}.", string.Empty,
            GiverMobKey: "vesh",
            TurninMobKey: "vesh",
            RequiredItemKey: requiredItem,
            RequiredCount: 1,
            RewardXp: 10,
            RewardGold: 0,
            RewardItemKey: null,
            RewardItemCount: 1,
            RewardFlagKey: null,
            PrerequisiteQuestKeys: [],
            IsRepeatable: false,
            AutoStart: false,
            Paths: [.. paths ?? []],
            Dialogue: new Dictionary<string, string>(StringComparer.Ordinal),
            SortOrder: 0);

    /// <summary>A smith standing in West, offering whatever quests the caller defines.</summary>
    private static (WorldHarness Harness, PlayerActor Actor) AtTheSmith(
        CharacterPath path,
        params UpsertQuest[] quests)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("vesh", West, name: "vesh");

        foreach (var quest in quests)
        {
            harness.Mutate(quest);
        }

        return (harness, harness.AddPlayer("Kaeda", West, path: path, level: 20));
    }

    /// <summary>
    /// Hears what Vesh has to say, then asks for each named quest by key.
    /// </summary>
    /// <remarks>
    /// <c>talk</c> stopped starting quests by itself (PLAN.md §4.9), so the gate is now tested the
    /// harder way round: the character does not merely fail to be offered the other three chains,
    /// they <b>ask for them by name and are refused</b>. A gate that only stopped advertising
    /// would pass the old shape of these tests and fail these.
    /// </remarks>
    private static string Talk(WorldHarness harness, PlayerActor actor, params string[] askFor)
    {
        harness.Drain(actor);
        harness.Execute(actor, "talk vesh");

        foreach (var key in askFor)
        {
            harness.Execute(actor, $"talk vesh {key}");
        }

        return harness.DrainText(actor);
    }

    /// <summary>The four epic chains, as keys — what a character asks for and is judged on.</summary>
    private static readonly string[] AllFour = ["e1-adept", "e1-hallow", "e1-temper", "e1-warden"];

    private static IReadOnlyList<string> Journal(WorldHarness harness, PlayerActor actor) =>
        [.. harness.World.QuestsFor(actor.CharacterId).Select(q => q.QuestKey)];

    // -----------------------------------------------------------------------
    // Who is offered what
    // -----------------------------------------------------------------------

    /// <summary>The bug, stated: one smith, four chains, one character.</summary>
    [Fact]
    public void One_giver_with_four_path_chains_hands_out_only_the_matching_one()
    {
        var (harness, actor) = AtTheSmith(
            CharacterPath.Temper,
            Quest("e1-adept", "The Stormrod", [CharacterPath.Adept]),
            Quest("e1-hallow", "The Hearthcenser", [CharacterPath.Hallow]),
            Quest("e1-temper", "The Quiet Knife", [CharacterPath.Temper]),
            Quest("e1-warden", "The Oathmaul", [CharacterPath.Warden]));

        Talk(harness, actor, AllFour);

        Assert.Equal(["e1-temper"], Journal(harness, actor));
    }

    [Theory]
    [InlineData(CharacterPath.Adept, "e1-adept")]
    [InlineData(CharacterPath.Hallow, "e1-hallow")]
    [InlineData(CharacterPath.Temper, "e1-temper")]
    [InlineData(CharacterPath.Warden, "e1-warden")]
    public void Each_path_gets_its_own_chain_and_no_other(CharacterPath path, string expected)
    {
        var (harness, actor) = AtTheSmith(
            path,
            Quest("e1-adept", "The Stormrod", [CharacterPath.Adept]),
            Quest("e1-hallow", "The Hearthcenser", [CharacterPath.Hallow]),
            Quest("e1-temper", "The Quiet Knife", [CharacterPath.Temper]),
            Quest("e1-warden", "The Oathmaul", [CharacterPath.Warden]));

        Talk(harness, actor, AllFour);

        Assert.Equal([expected], Journal(harness, actor));
    }

    /// <summary>
    /// <b>The property that matters most.</b> An unrestricted quest is offered to everyone, exactly
    /// as it was before this field existed — a gate that quietly narrowed the other fifteen quests
    /// would be a worse bug than the one it fixes.
    /// </summary>
    [Theory]
    [InlineData(CharacterPath.Adept)]
    [InlineData(CharacterPath.Hallow)]
    [InlineData(CharacterPath.Temper)]
    [InlineData(CharacterPath.Warden)]
    public void A_quest_with_no_paths_is_offered_to_anyone(CharacterPath path)
    {
        var (harness, actor) = AtTheSmith(path, Quest("road-out", "The Road Out"));

        Talk(harness, actor, "road-out");

        Assert.Equal(["road-out"], Journal(harness, actor));
    }

    /// <summary>A list, not one Path: "the two martial ones" is as real a case as one.</summary>
    [Fact]
    public void A_quest_may_name_more_than_one_path()
    {
        var martial = new[] { CharacterPath.Warden, CharacterPath.Temper };

        var (forBlade, temper) = AtTheSmith(CharacterPath.Temper, Quest("blades", "Tempers", martial));
        Talk(forBlade, temper, "blades");
        Assert.Equal(["blades"], Journal(forBlade, temper));

        var (forAdept, adept) = AtTheSmith(CharacterPath.Adept, Quest("blades", "Tempers", martial));
        Talk(forAdept, adept, "blades");
        Assert.Empty(Journal(forAdept, adept));
    }

    /// <summary>
    /// A giver with nothing for you says so, rather than silently doing nothing — the same answer
    /// they give when they have no quests at all.
    /// </summary>
    [Fact]
    public void A_giver_with_nothing_for_your_path_is_not_silent()
    {
        var (harness, actor) = AtTheSmith(
            CharacterPath.Adept, Quest("e1-temper", "The Quiet Knife", [CharacterPath.Temper]));

        Assert.False(string.IsNullOrWhiteSpace(Talk(harness, actor)));
    }

    // -----------------------------------------------------------------------
    // Someone already holding one
    // -----------------------------------------------------------------------

    /// <summary>
    /// A character who took the quest before the gate existed keeps it — nothing is removed from a
    /// journal behind their back — but is told plainly, and pointed at <c>abandon</c>.
    /// </summary>
    [Fact]
    public void A_quest_already_held_is_explained_rather_than_deleted()
    {
        // Offered while unrestricted, then restricted underneath them - which is exactly what an
        // import of the newly-tagged content does to every character mid-chain.
        var (harness, actor) = AtTheSmith(CharacterPath.Temper, Quest("e1-adept", "The Stormrod"));
        Talk(harness, actor, "e1-adept");
        Assert.Equal(["e1-adept"], Journal(harness, actor));

        harness.Mutate(Quest("e1-adept", "The Stormrod", [CharacterPath.Adept]));

        var said = Talk(harness, actor);

        Assert.Contains("never yours to finish", said, StringComparison.Ordinal);
        Assert.Contains("abandon", said, StringComparison.Ordinal);
        Assert.Equal(["e1-adept"], Journal(harness, actor));
    }

    /// <summary>
    /// <b>And it cannot be handed in.</b> Refusing only the offer would leave every character who
    /// already holds the wrong chain able to collect the reward — which is the actual damage, since
    /// the item is Path-locked, lore and no-drop.
    /// </summary>
    [Fact]
    public void A_quest_for_another_path_cannot_be_handed_in()
    {
        var (harness, actor) = AtTheSmith(
            CharacterPath.Temper, Quest("e1-adept", "The Stormrod", requiredItem: "ember"));

        var ember = new ItemTemplate
        {
            Key = "ember",
            Name = "a banked ember",
            Description = "Warm.",
            Icon = "*",
        };

        harness.ItemTemplates.Put(ember);

        Talk(harness, actor, "e1-adept");
        Assert.Equal(["e1-adept"], Journal(harness, actor));

        harness.Mutate(Quest("e1-adept", "The Stormrod", [CharacterPath.Adept], requiredItem: "ember"));
        harness.GiveItem(actor, ember);

        harness.Drain(actor);
        harness.Execute(actor, "give ember vesh");
        var said = harness.DrainText(actor);

        Assert.Contains("never yours to finish", said, StringComparison.Ordinal);

        // Still Active, and the ember is still theirs - a refused turn-in must not quietly eat the
        // item on the way out.
        var state = harness.World.GetQuestState(actor.CharacterId, "e1-adept");
        Assert.Equal(QuestStatus.Active, state!.Status);
        Assert.Contains(harness.World.InventoryOf(actor.CharacterId), i => i.TemplateKey == "ember");
    }

    /// <summary>The escape hatch works, so the journal is clearable rather than permanent.</summary>
    [Fact]
    public void Abandon_clears_a_quest_the_gate_now_refuses()
    {
        var (harness, actor) = AtTheSmith(CharacterPath.Temper, Quest("e1-adept", "The Stormrod"));
        Talk(harness, actor, "e1-adept");

        harness.Mutate(Quest("e1-adept", "The Stormrod", [CharacterPath.Adept]));

        harness.Drain(actor);
        harness.Execute(actor, "abandon The Stormrod");

        Assert.DoesNotContain(
            harness.World.QuestsFor(actor.CharacterId),
            q => q.QuestKey == "e1-adept" && q.Status == QuestStatus.Active);
    }
}
