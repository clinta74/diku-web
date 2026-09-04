using Muwbta.Domain.Characters;
using Muwbta.Domain.Quests;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Commands;

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
        return (harness, harness.AddPlayer("Kael", Room, path: CharacterPath.Temper, level: 10));
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

    /// <summary>A giver whose offer marks the words the errand is about.</summary>
    private static WorldHarness WithMarkedGiver(
        WorldHarness harness, string offer = "Somebody is missing those <things>.")
        => WithGiver(harness, offer);

    /// <summary>The spans of the last thing said, so a link can be told from the prose.</summary>
    private static IReadOnlyList<TextSpan> Spans(
        WorldHarness harness, PlayerActor actor, string command)
    {
        harness.Drain(actor);
        harness.Execute(actor, command);
        return harness.DrainSpans(actor);
    }

    /// <summary>The one span carrying a command, or null when the line offers nothing to click.</summary>
    private static TextSpan? Link(IReadOnlyList<TextSpan> spans) =>
        spans.SingleOrDefault(span => span.C is not null);

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

    /// <summary>
    /// The parenthetical is clickable, and <b>runs exactly the text it shows</b>.
    /// </summary>
    /// <remarks>
    /// The equality holds <em>here</em> because this span displays a command: the client echoes
    /// what it sends, so a span reading "talk elder ledger" that fired something else would put a
    /// string in the transcript the player never typed. A marked word in the prose displays a
    /// word rather than a command, so the two differ by design — see
    /// <see cref="Clicking_marked_words_shows_the_command_they_stand_for"/>, where the echo is
    /// what teaches the syntax.
    /// </remarks>
    [Fact]
    public void The_command_in_the_offer_is_clickable_and_runs_what_it_says()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        var span = Link(Spans(harness, actor, "talk elder"));

        Assert.NotNull(span);
        Assert.Equal(span.T, span.C);
        Assert.StartsWith("talk elder", span.C!, StringComparison.Ordinal);
    }

    /// <summary>And what it runs actually takes the quest, rather than merely looking right.</summary>
    [Fact]
    public void Clicking_the_offer_takes_the_quest()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        harness.Drain(actor);
        harness.Execute(actor, "talk elder");

        var command = harness.DrainSpans(actor).First(span => span.C is not null).C!;
        harness.Execute(actor, command);

        Assert.True(OnQuest(harness, actor, "fetch-ledger"));
    }

    /// <summary>
    /// The words around it stay prose, so a client that renders no commands still shows a whole
    /// instruction rather than a sentence with a hole in it.
    /// </summary>
    [Fact]
    public void The_offer_reads_as_a_sentence_without_the_link()
    {
        var (harness, actor) = Ready();
        WithGiver(harness);

        var said = Say(harness, actor, "talk elder");

        Assert.Contains("fetch-ledger — 'talk elder", said, StringComparison.Ordinal);
        Assert.Contains("' to take it on.)", said, StringComparison.Ordinal);
    }

    /// <summary>Nothing on the table, nothing to click.</summary>
    [Fact]
    public void A_mob_with_nothing_to_offer_sends_no_command_span()
    {
        var (harness, actor) = Ready();
        harness.AddMob("corun", Room, name: "Corun");

        harness.Drain(actor);
        harness.Execute(actor, "talk corun");

        Assert.DoesNotContain(harness.DrainSpans(actor), span => span.C is not null);
    }

    // -----------------------------------------------------------------------
    // Marked in the prose
    // -----------------------------------------------------------------------

    /// <summary>
    /// The words the giver marked are the link, sitting in the sentence rather than after it.
    /// </summary>
    [Fact]
    public void Marked_words_in_the_offer_are_the_link()
    {
        var (harness, actor) = Ready();
        WithMarkedGiver(harness);

        var span = Link(Spans(harness, actor, "talk elder"));

        Assert.NotNull(span);
        Assert.Equal("things", span.T);
        Assert.Equal("talk elder things", span.C);
    }

    /// <summary>
    /// The label is a word and the command is a command, which is the rule the parenthetical
    /// cannot follow and this one must.
    /// </summary>
    /// <remarks>
    /// Clicking sends the command, and the client echoes what it sends — so the player who clicks
    /// "things" watches "talk elder things" appear in their own transcript. That is the entire
    /// discoverability argument: the link teaches the sentence rather than replacing it.
    /// </remarks>
    [Fact]
    public void Clicking_marked_words_shows_the_command_they_stand_for()
    {
        var (harness, actor) = Ready();
        WithMarkedGiver(harness);

        var span = Link(Spans(harness, actor, "talk elder"));

        Assert.NotNull(span);
        Assert.NotEqual(span.T, span.C);

        harness.Execute(actor, span.C!);

        Assert.True(OnQuest(harness, actor, "fetch-ledger"));
    }

    /// <summary>The marker never reaches the player as punctuation.</summary>
    [Fact]
    public void The_brackets_are_not_spoken()
    {
        var (harness, actor) = Ready();
        WithMarkedGiver(harness);

        var said = Say(harness, actor, "talk elder");

        Assert.Equal("Somebody is missing those things.", said);
    }

    /// <summary>
    /// And with the link carrying the instruction, the stage direction goes away.
    /// </summary>
    [Fact]
    public void A_marked_offer_drops_the_parenthetical()
    {
        var (harness, actor) = Ready();
        WithMarkedGiver(harness);

        Assert.DoesNotContain("to take it on", Say(harness, actor, "talk elder"), StringComparison.Ordinal);
    }

    /// <summary>The words either side are prose, and only the marked ones are not.</summary>
    [Fact]
    public void Only_the_marked_words_are_clickable()
    {
        var (harness, actor) = Ready();
        WithMarkedGiver(harness);

        var spans = Spans(harness, actor, "talk elder");

        Assert.Equal(
            ["Somebody is missing those ", "things", "."],
            spans.Select(span => span.T));
        Assert.Equal(["things"], spans.Where(span => span.C is not null).Select(span => span.T));
    }

    /// <summary>
    /// The marked words are the keyword, so they work typed as well as clicked.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the keyword is parsed out of the prose rather than authored in a
    /// field of its own: a separate list can name a word the sentence does not contain, and
    /// nothing downstream is in a position to notice.
    /// </remarks>
    [Fact]
    public void Marked_words_can_be_typed_as_well_as_clicked()
    {
        var (harness, actor) = Ready();
        WithMarkedGiver(harness);

        Say(harness, actor, "talk elder things");

        Assert.True(OnQuest(harness, actor, "fetch-ledger"));
    }

    /// <summary>
    /// A marker that does not lead back to its own quest is never rendered as a link.
    /// </summary>
    /// <remarks>
    /// Two errands on one table marking the same word: the click could only land on one of them,
    /// so neither is linked and both fall back to naming themselves. The bundle validator refuses
    /// this at import — this is what the engine does with content that got in anyway, and the
    /// point is that the failure is a missing link rather than the wrong quest starting.
    /// </remarks>
    [Fact]
    public void A_marker_that_could_mean_two_quests_is_not_a_link()
    {
        var (harness, actor) = Ready();
        WithMarkedGiver(harness);
        harness.DefineQuest(
            "fetch-crate",
            giverMobKey: "elder",
            requiredItemKey: "ledger",
            dialogue: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["giverOffer"] = "And somebody wants those <things> too.",
            });

        var spans = Spans(harness, actor, "talk elder");
        var commands = spans.Where(span => span.C is not null).Select(span => span.C!).ToList();

        Assert.DoesNotContain("talk elder things", commands);
        Assert.Equal(2, commands.Count);

        // Both are still takeable, each by a parenthetical that names it unambiguously.
        harness.Execute(actor, commands[0]);
        harness.Execute(actor, commands[1]);

        Assert.True(OnQuest(harness, actor, "fetch-ledger"));
        Assert.True(OnQuest(harness, actor, "fetch-crate"));
    }

    /// <summary>
    /// A marker nobody closed leaves the line readable, brackets and all.
    /// </summary>
    /// <remarks>
    /// Falling open rather than swallowing the rest of the sentence: the builder sees their own
    /// mistake in the room, and the quest is still takeable through the parenthetical. The import
    /// refuses it long before this, which is where it should be caught.
    /// </remarks>
    [Fact]
    public void A_broken_marker_leaves_the_sentence_alone()
    {
        var (harness, actor) = Ready();
        WithGiver(harness, "Somebody is missing those <things.");

        var said = Say(harness, actor, "talk elder");

        Assert.Contains("those <things.", said, StringComparison.Ordinal);
        Assert.Contains("to take it on", said, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Being addressed
    // -----------------------------------------------------------------------

    /// <summary>
    /// Somebody named and then described is addressed by their name.
    /// </summary>
    /// <remarks>
    /// The last word used to be the address, on the reasoning that English puts the noun last.
    /// It told players to type <c>talk house</c> at Deacon Pell of Ilvaro's house — six of the
    /// eight givers in the Reaches, including the one who hands out the first quest in the game.
    /// </remarks>
    [Theory]
    [InlineData("Deacon Pell of Ilvaro's house", "pell")]
    [InlineData("Vesh, who follows the gates", "vesh")]
    [InlineData("Sister Aveth, who was expelled", "aveth")]
    [InlineData("Keeper Adda of Sulveth's stone", "adda")]
    [InlineData("Ista Roan, deep foreman", "foreman")]
    [InlineData("a bar maiden", "maiden")]
    public void A_giver_is_addressed_by_their_name_and_not_their_description(
        string name, string expected)
    {
        var (harness, actor) = Ready();
        harness.AddMob("giver", Room, name: name);
        harness.DefineQuest(
            "fetch-ledger",
            giverMobKey: "giver",
            dialogue: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["giverOffer"] = "Somebody is missing those <things>.",
            });

        var span = Link(Spans(harness, actor, $"talk {expected}"));

        Assert.NotNull(span);
        Assert.Equal($"talk {expected} things", span.C);
    }

    /// <summary>
    /// An address somebody else answers to first is lengthened until it reaches the right person.
    /// </summary>
    /// <remarks>
    /// The heuristic above is allowed to be wrong because nothing rests on it: every command is
    /// fed back through the same matching the player's typing goes through, and only kept if it
    /// comes back holding this mob and this quest.
    /// </remarks>
    [Fact]
    public void An_address_that_would_reach_somebody_else_is_lengthened()
    {
        var (harness, actor) = Ready();
        harness.AddMob("pell", Room, name: "Pell");
        harness.AddMob("deacon", Room, name: "Deacon Pell of Ilvaro's house");
        harness.DefineQuest(
            "fetch-ledger",
            giverMobKey: "deacon",
            dialogue: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["giverOffer"] = "Somebody is missing those <things>.",
            });

        var span = Link(Spans(harness, actor, "talk deacon"));

        Assert.NotNull(span);
        Assert.Equal("talk deacon pell things", span.C);
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
    /// Saying yes gets the instruction, not the pitch a second time.
    /// </summary>
    /// <remarks>
    /// The offer is what talked you into it and you have just read it; what you need on accepting
    /// is what to actually do. It is the same line the giver repeats when you come back and ask
    /// again, which is what makes talking to them a way to remember what you were doing.
    /// </remarks>
    [Fact]
    public void Accepting_says_what_to_do_rather_than_the_offer_again()
    {
        var (harness, actor) = Ready();
        harness.AddMob("elder", Room, name: "the village elder");
        harness.DefineQuest(
            "fetch-ledger",
            giverMobKey: "elder",
            dialogue: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["giverOffer"] = "Somebody is missing those <things>.",
                ["giverInProgress"] = "Six should do. Take them to the Keeper.",
            });

        var said = Say(harness, actor, "talk elder things");

        Assert.Contains("Six should do.", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Somebody is missing", said, StringComparison.Ordinal);
    }

    /// <summary>And asking again later says the same thing, which is the point of it.</summary>
    [Fact]
    public void The_giver_repeats_the_instruction_when_asked_again()
    {
        var (harness, actor) = Ready();
        harness.AddMob("elder", Room, name: "the village elder");
        harness.DefineQuest(
            "fetch-ledger",
            giverMobKey: "elder",
            dialogue: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["giverOffer"] = "Somebody is missing those <things>.",
                ["giverInProgress"] = "Six should do. Take them to the Keeper.",
            });

        Say(harness, actor, "talk elder things");

        Assert.Contains("Six should do.", Say(harness, actor, "talk elder"), StringComparison.Ordinal);
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
