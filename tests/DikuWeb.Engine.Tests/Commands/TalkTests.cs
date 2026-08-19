using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// Talking to somebody: what they say, and how a quest is taken on (PLAN.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// Two changes at once, because they are the same verb. <c>talk</c> used to <b>start</b> every
/// quest a giver had available, so there was no way to read what somebody wanted without taking
/// the job — and with all 35 authored quests at <c>autoStart: false</c>, that verb is how the
/// whole game progresses.
/// </para>
/// <para>
/// And it used to be for quest givers only. Everybody else got <em>"has nothing to say to you
/// about quests"</em>, which names the subsystem rather than the world — a shopkeeper said it
/// while standing behind a counter.
/// </para>
/// </remarks>
public sealed class TalkTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static (WorldHarness Harness, PlayerActor Actor) Ready()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return (harness, harness.AddPlayer("Kael", Room, path: CharacterPath.Shade, level: 10));
    }

    /// <summary>A giver with one errand on the table, authored the way the content is.</summary>
    private static WorldHarness WithGiver(WorldHarness harness, string offer = "Bring me the ledger.")
    {
        harness.AddMob("elder", Room, name: "the village elder");
        harness.DefineItem("ledger", "a dusty ledger", slot: null);
        harness.DefineQuest(
            "fetch-ledger",
            giverMobKey: "elder",
            requiredItemKey: "ledger",
            dialogue: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["giverOffer"] = offer,
            });

        return harness;
    }

    private static string Say(WorldHarness harness, PlayerActor actor, string command)
    {
        harness.Drain(actor);
        harness.Execute(actor, command);
        return harness.DrainText(actor);
    }

    private static bool OnQuest(WorldHarness harness, PlayerActor actor, string key) =>
        harness.World.GetQuestState(actor.CharacterId, key) is { Status: QuestStatus.Active };

    // -----------------------------------------------------------------------
    // Offering, without taking
    // -----------------------------------------------------------------------

    /// <summary><b>The change, stated.</b> Hearing what somebody wants does not commit you.</summary>
    [Fact]
    public void Talking_to_a_giver_no_longer_starts_the_quest()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        var said = Say(harness, actor, "talk elder");

        Assert.Contains("Bring me the ledger.", said, StringComparison.Ordinal);
        Assert.False(OnQuest(harness, actor, "fetch-ledger"));
    }

    /// <summary>And it can be heard twice, which is the point of it not committing.</summary>
    [Fact]
    public void The_offer_can_be_heard_again()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        Say(harness, actor, "talk elder");

        Assert.Contains(
            "Bring me the ledger.", Say(harness, actor, "talk elder"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The offer says how to say yes. Without it the mechanism is a guessing game — which is the
    /// classic failure of keyword dialogue and the one thing this had to avoid.
    /// </summary>
    [Fact]
    public void The_offer_names_the_command_that_takes_it()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        var said = Say(harness, actor, "talk elder");

        Assert.Contains("talk elder", said, StringComparison.Ordinal);
        Assert.Contains("to take it on", said, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Taking it on
    // -----------------------------------------------------------------------

    [Fact]
    public void Answering_the_giver_starts_the_quest()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        Say(harness, actor, "talk elder fetch-ledger");

        Assert.True(OnQuest(harness, actor, "fetch-ledger"));
    }

    /// <summary>
    /// Any word of the name or key reaches it, through the ranking <c>NameMatch</c> already does —
    /// which is why no keyword field had to be authored for this to work.
    /// </summary>
    [Theory]
    [InlineData("fetch-ledger")]
    [InlineData("ledger")]
    [InlineData("fetch")]
    public void Any_word_of_the_quest_reaches_it(string word)
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        Say(harness, actor, $"talk elder {word}");

        Assert.True(OnQuest(harness, actor, "fetch-ledger"));
    }

    /// <summary>A sentence works, because its words are searched the same way a keyword is.</summary>
    [Fact]
    public void A_whole_sentence_reaches_the_quest_its_words_name()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        Say(harness, actor, "talk elder I will find your ledger");

        Assert.True(OnQuest(harness, actor, "fetch-ledger"));
    }

    /// <summary>A giver addressed by a multi-word name still splits correctly.</summary>
    [Fact]
    public void A_multi_word_name_is_split_from_what_was_said_to_them()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);
        harness.AddMob("maiden", Room, name: "bar maiden");

        // "bar maiden" is the name; "ledger" is the speech. Nothing but trying every split point
        // can tell those apart.
        Say(harness, actor, "talk village elder ledger");

        Assert.True(OnQuest(harness, actor, "fetch-ledger"));
    }

    /// <summary>Saying something they have no answer for is not silence.</summary>
    [Fact]
    public void A_word_that_matches_nothing_is_answered()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        var said = Say(harness, actor, "talk elder turnips");

        Assert.Contains("does not know what you mean", said, StringComparison.Ordinal);
        Assert.False(OnQuest(harness, actor, "fetch-ledger"));
    }

    /// <summary>
    /// Naming a quest you are already on says so, rather than pretending not to understand — it is
    /// the likeliest reason a word that worked once stops working.
    /// </summary>
    [Fact]
    public void Asking_twice_says_you_are_already_on_it()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);
        Say(harness, actor, "talk elder ledger");

        Assert.Contains(
            "already on", Say(harness, actor, "talk elder ledger"), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Everybody else
    // -----------------------------------------------------------------------

    /// <summary>
    /// A shopkeeper is pointed at their own counter. <c>list</c> is three characters that a player
    /// has no way to discover from a room description.
    /// </summary>
    [Fact]
    public void A_shopkeeper_mentions_the_counter()
    {
        var (harness, actor) = Ready();
        harness.AddMob("berrin", Room, name: "Berrin", behavior: WorldHarness.AsPersisted(
            new Dictionary<string, object>
            {
                ["shopkeeper"] = true,
                ["type"] = "npc",
                ["sells"] = new List<object> { "ledger" },
            }));

        Assert.Contains("list", Say(harness, actor, "talk berrin"), StringComparison.Ordinal);
    }

    /// <summary>An authored greeting is what an ordinary NPC says.</summary>
    [Fact]
    public void An_npc_says_its_authored_greeting()
    {
        var (harness, actor) = Ready();
        harness.AddMob("corun", Room, name: "Corun", behavior: WorldHarness.AsPersisted(
            new Dictionary<string, object>
            {
                ["type"] = "npc",
                ["greeting"] = new List<object> { "Cold out, isn't it." },
            }));

        Assert.Contains(
            "Cold out, isn't it.", Say(harness, actor, "talk corun"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Authored through <see cref="WorldHarness.AsPersisted"/>, because a greeting list out of
    /// jsonb arrives as <c>JsonElement</c> — the one bug class this codebase keeps rediscovering,
    /// and the reason emotes have the same test.
    /// </summary>
    [Fact]
    public void A_greeting_survives_the_jsonb_round_trip()
    {
        var (harness, actor) = Ready();
        harness.AddMob("corun", Room, name: "Corun", behavior: WorldHarness.AsPersisted(
            new Dictionary<string, object>
            {
                ["type"] = "npc",
                ["greeting"] = new List<object> { "Mind the step." },
            }));

        Assert.Contains(
            "Mind the step.", Say(harness, actor, "talk corun"), StringComparison.Ordinal);
    }

    /// <summary>A mob with nothing authored still answers, and not in engine-speak.</summary>
    [Fact]
    public void A_mob_with_nothing_authored_still_says_something()
    {
        var (harness, actor) = Ready();
        harness.AddMob("corun", Room, name: "Corun");

        var said = Say(harness, actor, "talk corun");

        Assert.False(string.IsNullOrWhiteSpace(said));
        Assert.DoesNotContain("about quests", said, StringComparison.Ordinal);
    }

    /// <summary>A giver who has nothing for you right now falls through to small talk.</summary>
    [Fact]
    public void A_giver_with_nothing_available_falls_through_to_small_talk()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        // Prerequisite that will never be met, so the quest exists and is not on the table.
        harness.Quests.Get("fetch-ledger")!.PrerequisiteQuestKeys = ["nothing.named-this"];

        Assert.False(string.IsNullOrWhiteSpace(Say(harness, actor, "talk elder")));
    }

    [Fact]
    public void Talking_to_somebody_who_is_not_here_says_so()
    {
        var (harness, actor) = Ready();

        Assert.Contains(
            "You don't see", Say(harness, actor, "talk nobody"), StringComparison.Ordinal);
    }
}
