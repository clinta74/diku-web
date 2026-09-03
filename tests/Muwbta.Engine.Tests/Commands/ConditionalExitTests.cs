using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Exits that refuse people — a locked door, or a gate between Reaches (PLAN.md §4.15).
/// </summary>
/// <remarks>
/// The two requirement kinds are here for opposite reasons and both are tested as such: a flag
/// cannot be taken off a character, so it is what attunement to a realm is; an item can be dropped,
/// stolen, or left in a chest, so it is what a key is. A test suite that only proved "it refuses"
/// would not notice if one of them started behaving like the other.
/// </remarks>
public sealed class ConditionalExitTests
{
    private const string Flag = "attuned.grask";

    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    /// <summary>A world where walking west out of <see cref="East"/> is gated.</summary>
    private static WorldHarness Gated(
        string? flag = null,
        string? item = null,
        string? refusal = null)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.Mutate(new SetExit(East, Direction.West, West, flag, item, refusal));
        return harness;
    }

    // -----------------------------------------------------------------------
    // A flag
    // -----------------------------------------------------------------------

    [Fact]
    public void Without_the_flag_you_do_not_pass()
    {
        var harness = Gated(flag: Flag);
        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "west");

        Assert.Equal(East, actor.RoomKey);
    }

    [Fact]
    public void With_the_flag_you_do()
    {
        var harness = Gated(flag: Flag);
        var actor = harness.AddPlayer("Bram", East);
        actor.Character.Flags.Add(Flag);

        harness.Execute(actor, "west");

        Assert.Equal(West, actor.RoomKey);
    }

    [Fact]
    public void A_different_flag_is_not_the_flag()
    {
        // Ordinal comparison, so no near-miss counts. Attunement to one Reach is not attunement
        // to another, and this is the assertion that says so.
        var harness = Gated(flag: Flag);
        var actor = harness.AddPlayer("Bram", East);
        actor.Character.Flags.Add("attuned.azhen");

        harness.Execute(actor, "west");

        Assert.Equal(East, actor.RoomKey);
    }

    // -----------------------------------------------------------------------
    // An item
    // -----------------------------------------------------------------------

    [Fact]
    public void Without_the_key_you_do_not_pass()
    {
        var harness = Gated(item: "brass-key");
        harness.DefineItem("brass-key", "brass key", null);
        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "west");

        Assert.Equal(East, actor.RoomKey);
    }

    [Fact]
    public void Carrying_the_key_opens_it()
    {
        var harness = Gated(item: "brass-key");
        var key = harness.DefineItem("brass-key", "brass key", null);
        var actor = harness.AddPlayer("Bram", East);
        harness.GiveItem(actor, key);

        harness.Execute(actor, "west");

        Assert.Equal(West, actor.RoomKey);
    }

    [Fact]
    public void Wearing_the_key_counts_as_carrying_it()
    {
        // Ownership rather than what is loose in the pack: a signet ring on a finger is still a
        // signet ring held, and a player should not have to unequip to open their own door.
        var harness = Gated(item: "signet-ring");
        var ring = harness.DefineItem("signet-ring", "signet ring", ItemSlot.Trinket);
        var actor = harness.AddPlayer("Bram", East);
        harness.Equip(actor, ring, ItemSlot.Trinket);

        harness.Execute(actor, "west");

        Assert.Equal(West, actor.RoomKey);
    }

    [Fact]
    public void The_key_is_not_consumed()
    {
        var harness = Gated(item: "brass-key");
        var key = harness.DefineItem("brass-key", "brass key", null);
        var actor = harness.AddPlayer("Bram", East);
        harness.GiveItem(actor, key);

        harness.Execute(actor, "west");

        Assert.Contains(
            harness.World.InventoryOf(actor.CharacterId),
            i => string.Equals(i.TemplateKey, "brass-key", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // Both, and what is said
    // -----------------------------------------------------------------------

    [Fact]
    public void Both_requirements_have_to_hold()
    {
        var harness = Gated(flag: Flag, item: "brass-key");
        var key = harness.DefineItem("brass-key", "brass key", null);
        var actor = harness.AddPlayer("Bram", East);
        harness.GiveItem(actor, key);

        harness.Execute(actor, "west");
        Assert.Equal(East, actor.RoomKey);

        actor.Character.Flags.Add(Flag);
        harness.Execute(actor, "west");
        Assert.Equal(West, actor.RoomKey);
    }

    [Fact]
    public void The_authored_refusal_is_what_you_are_told()
    {
        var harness = Gated(flag: Flag, refusal: "The gate does not know you.");
        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "west");

        Assert.Contains("does not know you", harness.DrainText(actor), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_one_authored_there_is_a_generic_line()
    {
        var harness = Gated(flag: Flag);
        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "west");

        Assert.Contains(ExitGate.GenericRefusal, harness.DrainText(actor), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unconditional_exit_is_untouched()
    {
        // The same exit as every test above, stated with no conditions at all - so this is also
        // the assertion that a SetExit carrying nothing leaves the way open rather than sealing it.
        var harness = Gated();
        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "west");

        Assert.Equal(West, actor.RoomKey);
    }

    [Fact]
    public void A_builder_is_not_exempt()
    {
        // `goto` already takes a builder anywhere, so nothing is lost by this - and it is the only
        // way an author discovers the flag key on their own gate has a typo in it.
        var harness = Gated(flag: Flag);
        var actor = harness.AddPlayer("Bram", East, role: Muwbta.Domain.Accounts.AccountRole.Builder);

        harness.Execute(actor, "west");

        Assert.Equal(East, actor.RoomKey);
    }

    // -----------------------------------------------------------------------
    // A gate refuses before it asks what is behind it
    // -----------------------------------------------------------------------

    [Fact]
    public void A_gate_to_nowhere_still_reads_as_a_gate()
    {
        // Checked before the destination resolves, so a locked door says it is locked whether or
        // not the builder has dug the room behind it yet (§7.4).
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.Mutate(new SetExit(
            East, Direction.North, RoomKey.Parse("test.zone.nowhere"), Flag, null, "It is locked."));

        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "north");

        Assert.Contains("It is locked.", harness.DrainText(actor), StringComparison.Ordinal);
    }
}
