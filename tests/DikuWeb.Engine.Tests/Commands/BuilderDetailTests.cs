using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// The builder-facing half of <c>examine</c> and <c>stats</c>: template keys, raw numbers, and
/// a link that opens the thing in the builder.
/// </summary>
/// <remarks>
/// The gating is what these mostly cover. A player must not see a template key or a builder
/// path - not because either is dangerous, but because the whole point of the block is that it
/// is scaffolding, and scaffolding shown to everyone is just noise in the prose.
/// </remarks>
public sealed class BuilderDetailTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>Every builder path any span in the last batch carried.</summary>
    private static List<string> LinksFor(WorldHarness harness, Engine.World.PlayerActor actor) =>
        [.. harness.Drain(actor)
            .Where(e => e.Type == Engine.Protocol.EventTypes.Text)
            .SelectMany(e => ((Engine.Protocol.TextPayload)e.Payload).Spans)
            .Select(s => s.B)
            .OfType<string>()];

    // -----------------------------------------------------------------------
    // Examining an item
    // -----------------------------------------------------------------------

    [Fact]
    public void A_builder_examining_an_item_is_shown_its_template_key()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        var blade = harness.DefineItem("rusted-blade", "rusted blade", ItemSlot.MainHand);
        harness.GiveItem(kael, blade);
        harness.Drain(kael);

        harness.Execute(kael, "examine blade");

        Assert.Contains("[rusted-blade]", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_builder_examining_an_item_is_given_a_link_to_it()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        var blade = harness.DefineItem("rusted-blade", "rusted blade", ItemSlot.MainHand);
        harness.GiveItem(kael, blade);
        harness.Drain(kael);

        harness.Execute(kael, "examine blade");

        Assert.Contains("/builder/items/rusted-blade", LinksFor(harness, kael));
    }

    [Fact]
    public void A_player_examining_an_item_sees_neither_key_nor_link()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var blade = harness.DefineItem("rusted-blade", "rusted blade", ItemSlot.MainHand);
        harness.GiveItem(kael, blade);
        harness.Drain(kael);

        harness.Execute(kael, "examine blade");

        var events = harness.Drain(kael);
        var text = string.Concat(events
            .Where(e => e.Type == Engine.Protocol.EventTypes.Text)
            .SelectMany(e => ((Engine.Protocol.TextPayload)e.Payload).Spans)
            .Select(s => s.T));

        Assert.DoesNotContain("[rusted-blade]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/builder/", text, StringComparison.Ordinal);
        Assert.Empty(events
            .Where(e => e.Type == Engine.Protocol.EventTypes.Text)
            .SelectMany(e => ((Engine.Protocol.TextPayload)e.Payload).Spans)
            .Select(s => s.B)
            .OfType<string>());
    }

    [Fact]
    public void A_player_still_sees_the_prose()
    {
        // Gating the builder block must not gate the description with it.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var blade = harness.DefineItem(
            "rusted-blade", "rusted blade", ItemSlot.MainHand, "Pitted along the edge.");
        harness.GiveItem(kael, blade);
        harness.Drain(kael);

        harness.Execute(kael, "examine blade");

        Assert.Contains("Pitted along the edge.", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_examined_quest_item_says_that_it_is_bound()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var letter = harness.DefineItem("sealed-letter", "sealed letter", slot: null);
        harness.GiveItem(
            kael,
            letter,
            WorldHarness.AsPersisted(new Dictionary<string, object> { ["questItem"] = true }));
        harness.Drain(kael);

        harness.Execute(kael, "examine letter");

        Assert.Contains("bound to a quest", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_quest_item_is_tagged_in_the_inventory_list()
    {
        // The refusals already existed - a quest item cannot be sold or destroyed - but nothing
        // said which items those were until you examined them one at a time. A pack with three
        // things in it that will not shift should be readable at a glance.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var letter = harness.DefineItem("sealed-letter", "sealed letter", slot: null);
        var stick = harness.DefineItem("sharp-stick", "sharp stick", slot: null);
        harness.GiveItem(
            kael,
            letter,
            WorldHarness.AsPersisted(new Dictionary<string, object> { ["questItem"] = true }));
        harness.GiveItem(kael, stick);
        harness.Drain(kael);

        harness.Execute(kael, "inventory");

        var text = harness.DrainText(kael);
        Assert.Contains("sealed letter (quest)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sharp stick (quest)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_worn_quest_item_keeps_its_tag()
    {
        // A quest reward with a slot is ordinary content, and the tag disappearing the moment it
        // is put on would be the same gap one layer down.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var circlet = harness.DefineItem("circlet", "plain circlet", ItemSlot.Head);
        harness.GiveItem(
            kael,
            circlet,
            WorldHarness.AsPersisted(new Dictionary<string, object> { ["questItem"] = true }));
        harness.Drain(kael);

        harness.Execute(kael, "wear circlet");
        harness.Drain(kael);
        harness.Execute(kael, "inventory");

        Assert.Contains("plain circlet (quest)", harness.DrainText(kael), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Examining a mob
    // -----------------------------------------------------------------------

    [Fact]
    public void A_mob_can_be_examined_at_all()
    {
        // Examine looked only at items, so examining the NPC in front of you reported that it
        // was not there.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("barkeep", Room, name: "barkeep");
        harness.Drain(kael);

        harness.Execute(kael, "examine barkeep");

        var text = harness.DrainText(kael);
        Assert.Contains("You examine", text, StringComparison.Ordinal);
        Assert.DoesNotContain("don't see", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_examined_mob_reports_its_condition_not_its_hit_points()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var rat = harness.AddMob("rat", Room, name: "rat", health: 100);
        rat.Vitals.Health = 40;
        harness.Drain(kael);

        harness.Execute(kael, "examine rat");

        var text = harness.DrainText(kael);
        Assert.Contains("badly hurt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("40/100", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(100, "unhurt")]
    [InlineData(80, "scratched")]
    [InlineData(60, "bleeding")]
    [InlineData(40, "badly hurt")]
    [InlineData(5, "barely standing")]
    public void An_examined_mobs_condition_tracks_its_health(int health, string expected)
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var rat = harness.AddMob("rat", Room, name: "rat", health: 100);
        rat.Vitals.Health = health;
        harness.Drain(kael);

        harness.Execute(kael, "examine rat");

        Assert.Contains(expected, harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_examined_npc_says_it_cannot_be_fought()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob(
            "barkeep",
            Room,
            name: "barkeep",
            behavior: WorldHarness.AsPersisted(new Dictionary<string, object> { ["type"] = "npc" }));
        harness.Drain(kael);

        harness.Execute(kael, "examine barkeep");

        Assert.Contains("not someone you can fight", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_examined_shopkeeper_points_at_the_list_command()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob(
            "barkeep",
            Room,
            name: "barkeep",
            behavior: WorldHarness.AsPersisted(new Dictionary<string, object> { ["shopkeeper"] = true }));
        harness.Drain(kael);

        harness.Execute(kael, "examine barkeep");

        // "The barkeep keeps a shop" - named rather than "They keep a shop", so the line agrees
        // with the condition line above it about what to call the mob.
        Assert.Contains("The barkeep keeps a shop", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Examining_a_mob_calls_it_the_same_thing_on_every_line()
    {
        // Reported from play. The condition line said "It is unhurt." and the disposition line
        // said "They are not someone you can fight." - two pronouns for one mob, a line apart.
        // Neither pronoun works for the whole roster either: "It" is wrong for the bar maiden,
        // "They" is wrong for a rat, and a template does not say which kind of thing it is.
        // Every line names the mob now, so there is nothing left to disagree about.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob(
            "oldman",
            Room,
            name: "old man",
            behavior: WorldHarness.AsPersisted(new Dictionary<string, object> { ["type"] = "npc" }));
        harness.Drain(kael);

        harness.Execute(kael, "examine old man");

        var text = harness.DrainText(kael);
        Assert.Contains("You examine the old man.", text, StringComparison.Ordinal);
        Assert.Contains("The old man is unhurt.", text, StringComparison.Ordinal);
        Assert.Contains("The old man is not someone you can fight.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("They are", text, StringComparison.Ordinal);
        Assert.DoesNotContain("It is unhurt", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_builder_examining_a_mob_is_given_a_link_to_it()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        harness.AddMob("giant-rat", Room, name: "giant rat");
        harness.Drain(kael);

        harness.Execute(kael, "examine rat");

        Assert.Contains("/builder/mobs/giant-rat", LinksFor(harness, kael));
    }

    [Fact]
    public void A_player_examining_a_mob_gets_no_link()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("giant-rat", Room, name: "giant rat");
        harness.Drain(kael);

        harness.Execute(kael, "examine rat");

        Assert.Empty(LinksFor(harness, kael));
    }

    /// <summary>An item in hand wins over a mob of the same name, which is the commoner intent.</summary>
    [Fact]
    public void An_item_is_preferred_over_a_mob_with_the_same_name()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var effigy = harness.DefineItem("rat", "rat", slot: null, description: "A carved rat.");
        harness.GiveItem(kael, effigy);
        harness.AddMob("rat", Room, name: "rat");
        harness.Drain(kael);

        harness.Execute(kael, "examine rat");

        Assert.Contains("A carved rat.", harness.DrainText(kael), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Stats
    // -----------------------------------------------------------------------

    [Fact]
    public void A_builder_running_stats_gets_a_link_to_the_room_they_are_in()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        harness.Drain(kael);

        harness.Execute(kael, "stats");

        // Segments are slugged the way the client's toWorldPath builds them.
        Assert.Contains("/builder/world/test/zone/west/details", LinksFor(harness, kael));
    }

    [Fact]
    public void A_builder_running_stats_sees_the_zone_multipliers()
    {
        var harness = Loaded();
        harness.SetZoneMultipliers(m => m.Xp = 3m);
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        harness.Drain(kael);

        harness.Execute(kael, "stats");

        Assert.Contains("xp ×3", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_builder_running_stats_gets_a_link_for_each_equipped_item()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        var blade = harness.DefineItem("rusted-blade", "rusted blade", ItemSlot.MainHand);
        harness.Equip(kael, blade, ItemSlot.MainHand);
        harness.Drain(kael);

        harness.Execute(kael, "stats");

        Assert.Contains("/builder/items/rusted-blade", LinksFor(harness, kael));
    }

    // -----------------------------------------------------------------------
    // Layout
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every value in a block starts at the same column.
    /// </summary>
    /// <remarks>
    /// The scrollback is monospace with <c>white-space: pre-wrap</c>, so the padding survives to
    /// the screen and the labels line up. Unpadded, "xp:" and "spawner:" put their values seven
    /// columns apart and the block read as a ragged list rather than a table.
    /// </remarks>
    [Fact]
    public void The_examine_detail_block_lines_its_values_up()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        harness.AddMob("city-rat", Room, name: "city rat");
        harness.Drain(kael);

        harness.Execute(kael, "examine rat");

        var columns = ValueColumns(harness.DrainText(kael), after: "[city-rat]");

        Assert.NotEmpty(columns);
        Assert.Single(columns.Distinct());
    }

    [Fact]
    public void The_stats_builder_block_lines_its_values_up()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        var blade = harness.DefineItem("rusted-blade", "rusted blade", ItemSlot.MainHand);
        harness.Equip(kael, blade, ItemSlot.MainHand);
        harness.Drain(kael);

        harness.Execute(kael, "stats");

        // Scoped to the Builder section: the combat lines above it ("Dice:", "Speed:", "Armour:")
        // are prose with a colon in them, not a column, and were never meant to line up.
        var columns = ValueColumns(harness.DrainText(kael), after: "Builder");

        Assert.NotEmpty(columns);
        Assert.Single(columns.Distinct());
    }

    /// <summary>
    /// The column each "  label:   value" row's value begins at, for the block following
    /// <paramref name="after"/>. Only two-space-indented rows carrying a padded colon count,
    /// which is exactly the shape the detail blocks emit.
    /// </summary>
    private static List<int> ValueColumns(string text, string after)
    {
        var start = text.IndexOf(after, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No '{after}' block in:\n{text}");

        return [.. text[start..].Split('\n')
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Select(line => (Line: line, Colon: line.IndexOf(':', StringComparison.Ordinal)))
            .Where(x => x.Colon > 0 && x.Line.Length > x.Colon + 1 && x.Line[x.Colon + 1] == ' ')
            .Select(x => x.Line.Length - x.Line[(x.Colon + 1)..].TrimStart().Length)];
    }

    /// <summary>
    /// The template key heading is itself the link.
    /// </summary>
    /// <remarks>
    /// It is already on screen and already the thing a builder would go looking for, so a
    /// separate "Open in builder" row underneath was a line of pure scaffolding under every
    /// block - and it read as one more field rather than as an action.
    /// </remarks>
    [Fact]
    public void The_template_key_heading_is_the_link()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        harness.AddMob("city-rat", Room, name: "city rat");
        harness.Drain(kael);

        harness.Execute(kael, "examine rat");

        var linked = harness.Drain(kael)
            .Where(e => e.Type == Engine.Protocol.EventTypes.Text)
            .SelectMany(e => ((Engine.Protocol.TextPayload)e.Payload).Spans)
            .Where(s => s.B == "/builder/mobs/city-rat")
            .ToList();

        Assert.Equal("[city-rat]", Assert.Single(linked).T);
    }

    [Fact]
    public void The_detail_block_carries_no_separate_open_in_builder_row()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        harness.AddMob("city-rat", Room, name: "city rat");
        harness.Drain(kael);

        harness.Execute(kael, "examine rat");

        Assert.DoesNotContain("Open in builder", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void The_stats_room_and_item_keys_are_the_links()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        var blade = harness.DefineItem("rusted-blade", "rusted blade", ItemSlot.MainHand);
        harness.Equip(kael, blade, ItemSlot.MainHand);
        harness.Drain(kael);

        harness.Execute(kael, "stats");

        var linked = harness.Drain(kael)
            .Where(e => e.Type == Engine.Protocol.EventTypes.Text)
            .SelectMany(e => ((Engine.Protocol.TextPayload)e.Payload).Spans)
            .Where(s => s.B is not null)
            .ToDictionary(s => s.B!, s => s.T);

        Assert.Equal("test.zone.west", linked["/builder/world/test/zone/west/details"]);
        Assert.Equal("rusted-blade", linked["/builder/items/rusted-blade"]);
    }

    /// <summary>
    /// A link span carries its label and nothing else — no line breaks.
    /// </summary>
    /// <remarks>
    /// The client renders one as an inline-block <c>&lt;button&gt;</c>, and the scrollback's
    /// <c>white-space: pre-wrap</c> is inherited into it. A newline inside a link therefore makes
    /// the *button* two or three lines tall instead of breaking the line, and its label drops to
    /// the baseline of whatever preceded it — which put "Open in builder" beside the last data
    /// row with two blank lines above it. Breaks belong in the plain span before the link.
    /// </remarks>
    [Theory]
    [InlineData("examine rat")]
    [InlineData("stats")]
    public void No_link_span_contains_a_line_break(string command)
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Builder);
        var blade = harness.DefineItem("rusted-blade", "rusted blade", ItemSlot.MainHand);
        harness.Equip(kael, blade, ItemSlot.MainHand);
        harness.AddMob("city-rat", Room, name: "city rat");
        harness.Drain(kael);

        harness.Execute(kael, command);

        var links = harness.Drain(kael)
            .Where(e => e.Type == Engine.Protocol.EventTypes.Text)
            .SelectMany(e => ((Engine.Protocol.TextPayload)e.Payload).Spans)
            .Where(s => s.B is not null)
            .ToList();

        Assert.NotEmpty(links);
        Assert.All(links, span =>
            Assert.DoesNotContain('\n', span.T));
    }

    [Fact]
    public void A_player_running_stats_gets_no_builder_block()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.Drain(kael);

        harness.Execute(kael, "stats");

        var events = harness.Drain(kael);
        var spans = events
            .Where(e => e.Type == Engine.Protocol.EventTypes.Text)
            .SelectMany(e => ((Engine.Protocol.TextPayload)e.Payload).Spans)
            .ToList();

        Assert.DoesNotContain("Builder", string.Concat(spans.Select(s => s.T)), StringComparison.Ordinal);
        Assert.Empty(spans.Select(s => s.B).OfType<string>());
    }

    [Fact]
    public void An_admin_counts_as_a_builder()
    {
        // Roles are hierarchical; an admin editing content should not have to be demoted to see
        // the same block a builder gets.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Admin);
        harness.Drain(kael);

        harness.Execute(kael, "stats");

        Assert.NotEmpty(LinksFor(harness, kael));
    }

    [Fact]
    public void A_moderator_does_not_count_as_a_builder()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room, AccountRole.Moderator);
        harness.Drain(kael);

        harness.Execute(kael, "stats");

        Assert.Empty(LinksFor(harness, kael));
    }
}
