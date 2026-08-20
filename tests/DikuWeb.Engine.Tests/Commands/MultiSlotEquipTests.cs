using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// An item names every slot it fits, and a weapon may claim both hands (PLAN.md §4.19).
/// </summary>
/// <remarks>
/// <para>
/// Reported as "it's hard to get off-hand weapons"; the truth was that there were none. All 35
/// authored weapons were <c>MainHand</c> and all six off-hand items were shields or a torch, every
/// one of them with no attack speed - so <c>DualWield</c> (a Temper at level 3, a Warden at 5),
/// <c>Ambidextrous</c>, <c>OffHandDamageShare</c> and the whole second-strike path in
/// <c>CombatSystem</c> had nothing in the world that could reach them. The level-up line
/// <em>"You can strike with a weapon in your off hand"</em> named a thing a player could not do.
/// </para>
/// <para>
/// So the property under test is reachability as much as correctness: an either-hand weapon has to
/// actually arrive in the off hand, and the refusals around it have to say which hand they mean.
/// </para>
/// </remarks>
public sealed class MultiSlotEquipTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static readonly IReadOnlyList<ItemSlot> EitherHand =
        [ItemSlot.MainHand, ItemSlot.OffHand];

    private static (WorldHarness Harness, PlayerActor Actor) Armed(
        CharacterPath path = CharacterPath.Temper)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return (harness, harness.AddPlayer("Kaeda", Room, path: path, level: 20));
    }

    private static ItemTemplate Temper(WorldHarness harness, string key = "short-temper") =>
        harness.DefineWeapon(key, "a " + key.Replace('-', ' '), EitherHand, delayPulses: 6, verb: "slash");

    private static ItemTemplate Maul(WorldHarness harness) =>
        harness.DefineWeapon(
            "standing-maul", "a standing maul", [ItemSlot.MainHand],
            delayPulses: 10, verb: "crush", twoHanded: true);

    private static ItemTemplate Shield(WorldHarness harness) =>
        harness.DefineItem("plank-shield", "a banded plank shield", ItemSlot.OffHand);

    // -----------------------------------------------------------------------
    // Either hand
    // -----------------------------------------------------------------------

    /// <summary>Main hand first, because that is the order the slots are declared in.</summary>
    [Fact]
    public void An_either_hand_weapon_reaches_for_the_main_hand()
    {
        var (harness, actor) = Armed();
        var temper = harness.GiveItem(actor, Temper(harness));

        harness.Execute(actor, "wield short-temper");

        Assert.Equal(ItemSlot.MainHand, temper.EquippedSlot);
    }

    /// <summary><b>The point of the whole change.</b> A second temper lands in the off hand.</summary>
    [Fact]
    public void A_second_either_hand_weapon_goes_to_the_off_hand()
    {
        var (harness, actor) = Armed();
        var first = harness.GiveItem(actor, Temper(harness, "short-temper"));
        var second = harness.GiveItem(actor, Temper(harness, "keening-temper"));

        harness.Execute(actor, "wield short-temper");
        harness.Execute(actor, "wield keening-temper");

        Assert.Equal(ItemSlot.MainHand, first.EquippedSlot);
        Assert.Equal(ItemSlot.OffHand, second.EquippedSlot);
    }

    /// <summary>
    /// With the main hand already full of something that cannot leave it, the either-hand weapon
    /// still finds a home rather than being refused.
    /// </summary>
    [Fact]
    public void An_either_hand_weapon_settles_for_the_off_hand_when_the_main_is_taken()
    {
        var (harness, actor) = Armed();
        var spear = harness.DefineWeapon("vigil-spear", "a vigil spear", ItemSlot.MainHand, 8, "pierce");
        harness.Equip(actor, spear, ItemSlot.MainHand);

        var temper = harness.GiveItem(actor, Temper(harness));
        harness.Execute(actor, "wield short-temper");

        Assert.Equal(ItemSlot.OffHand, temper.EquippedSlot);
    }

    /// <summary>Both hands full names both, rather than only the first one tried.</summary>
    [Fact]
    public void With_both_hands_full_the_refusal_names_both()
    {
        var (harness, actor) = Armed();
        var spear = harness.DefineWeapon("vigil-spear", "a vigil spear", ItemSlot.MainHand, 8, "pierce");
        harness.Equip(actor, spear, ItemSlot.MainHand);
        harness.Equip(actor, Shield(harness), ItemSlot.OffHand);

        var temper = harness.GiveItem(actor, Temper(harness));
        harness.Drain(actor);
        harness.Execute(actor, "wield short-temper");

        var said = harness.DrainText(actor);

        Assert.Null(temper.EquippedSlot);
        Assert.Contains("main hand", said, StringComparison.Ordinal);
        Assert.Contains("off hand", said, StringComparison.Ordinal);
    }

    /// <summary>An either-hand weapon is still a weapon: it is wielded, not worn.</summary>
    [Fact]
    public void An_either_hand_weapon_cannot_be_worn()
    {
        var (harness, actor) = Armed();
        var temper = harness.GiveItem(actor, Temper(harness));
        harness.Drain(actor);

        harness.Execute(actor, "wear short-temper");

        Assert.Null(temper.EquippedSlot);
        Assert.Contains("wielding it instead", harness.DrainText(actor), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Both hands
    // -----------------------------------------------------------------------

    [Fact]
    public void A_two_handed_weapon_goes_in_the_main_hand()
    {
        var (harness, actor) = Armed();
        var maul = harness.GiveItem(actor, Maul(harness));

        harness.Execute(actor, "wield standing-maul");

        Assert.Equal(ItemSlot.MainHand, maul.EquippedSlot);
    }

    /// <summary>
    /// It is refused for the off hand it is about to claim, not for the main hand it wants - with
    /// the main hand empty, "you're already using your main hand" would be nonsense.
    /// </summary>
    [Fact]
    public void A_two_handed_weapon_is_refused_while_the_off_hand_is_full()
    {
        var (harness, actor) = Armed();
        harness.Equip(actor, Shield(harness), ItemSlot.OffHand);

        var maul = harness.GiveItem(actor, Maul(harness));
        harness.Drain(actor);
        harness.Execute(actor, "wield standing-maul");

        var said = harness.DrainText(actor);

        Assert.Null(maul.EquippedSlot);
        Assert.Contains("both hands", said, StringComparison.Ordinal);
        Assert.Contains("plank shield", said, StringComparison.Ordinal);
    }

    /// <summary>And nothing joins it afterwards - the other half of the same rule.</summary>
    [Fact]
    public void Nothing_can_be_wielded_alongside_a_two_handed_weapon()
    {
        var (harness, actor) = Armed();
        harness.Equip(actor, Maul(harness), ItemSlot.MainHand);

        var shield = harness.GiveItem(actor, Shield(harness));
        harness.Drain(actor);
        harness.Execute(actor, "wield plank-shield");

        var said = harness.DrainText(actor);

        Assert.Null(shield.EquippedSlot);
        Assert.Contains("both hands", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// Putting it down frees the hand it was denying, which is what makes the refusal a state
    /// rather than a wall.
    /// </summary>
    [Fact]
    public void Removing_a_two_handed_weapon_frees_the_off_hand()
    {
        var (harness, actor) = Armed();
        var maul = harness.GiveItem(actor, Maul(harness));
        harness.Execute(actor, "wield standing-maul");
        Assert.Equal(ItemSlot.MainHand, maul.EquippedSlot);

        harness.Execute(actor, "remove standing-maul");

        var shield = harness.GiveItem(actor, Shield(harness));
        harness.Execute(actor, "wield plank-shield");

        Assert.Equal(ItemSlot.OffHand, shield.EquippedSlot);
    }

    /// <summary>
    /// A worn item is not a held one. Armour is unaffected by a weapon claiming both hands, which
    /// is worth pinning because the guard reads the hands and armour is the larger neighbouring
    /// case.
    /// </summary>
    [Fact]
    public void A_two_handed_weapon_does_not_block_armour()
    {
        var (harness, actor) = Armed();
        harness.Equip(actor, Maul(harness), ItemSlot.MainHand);

        var cap = harness.GiveItem(actor, harness.DefineItem("leather-cap", "a leather cap", ItemSlot.Head));
        harness.Execute(actor, "wear leather-cap");

        Assert.Equal(ItemSlot.Head, cap.EquippedSlot);
    }

    // -----------------------------------------------------------------------
    // The single-slot world, unchanged
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>The property most of this file is insurance for.</b> A one-slot item behaves exactly as
    /// it did before slots were a list - a change that quietly loosened every item in the game
    /// would be worse than the gap it closed.
    /// </summary>
    [Theory]
    [InlineData(ItemSlot.Head)]
    [InlineData(ItemSlot.Chest)]
    [InlineData(ItemSlot.Hands)]
    [InlineData(ItemSlot.Legs)]
    [InlineData(ItemSlot.Feet)]
    [InlineData(ItemSlot.Trinket)]
    public void A_single_slot_item_still_goes_where_it_always_did(ItemSlot slot)
    {
        var (harness, actor) = Armed();
        var piece = harness.GiveItem(actor, harness.DefineItem("piece", "a piece of kit", slot));

        harness.Execute(actor, "wear piece");

        Assert.Equal(slot, piece.EquippedSlot);
    }

    [Fact]
    public void An_item_with_no_slots_is_still_refused_by_both_verbs()
    {
        var (harness, actor) = Armed();
        var rock = harness.GiveItem(actor, harness.DefineItem("rock", "a rock", slot: null));

        harness.Drain(actor);
        harness.Execute(actor, "wear rock");
        harness.Execute(actor, "wield rock");

        var said = harness.DrainText(actor);

        Assert.Null(rock.EquippedSlot);
        Assert.Equal(2, said.Split("isn't something you can equip").Length - 1);
    }

    /// <summary>
    /// A single-slot occupied refusal keeps its old wording, which named the slot. It is the
    /// message a player meets most often of any in this file.
    /// </summary>
    [Fact]
    public void A_full_single_slot_still_says_which_slot()
    {
        var (harness, actor) = Armed();
        harness.Equip(actor, harness.DefineItem("worn-cap", "a worn cap", ItemSlot.Head), ItemSlot.Head);

        harness.GiveItem(actor, harness.DefineItem("fine-cap", "a fine cap", ItemSlot.Head));
        harness.Drain(actor);
        harness.Execute(actor, "wear fine-cap");

        Assert.Contains("your head", harness.DrainText(actor), StringComparison.Ordinal);
    }
}
