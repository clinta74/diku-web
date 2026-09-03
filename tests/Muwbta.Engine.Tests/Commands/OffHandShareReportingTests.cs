using Muwbta.Domain.Abilities;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// The <c>stats</c> screen reports the off hand a character actually has (PLAN.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// An off hand deals a share of its damage that grows with level, so the number on the weapon is no
/// longer the number the hand rolls. That is exactly the shape of lie this screen was rewritten to
/// stop telling — its own comment says a weapon "showing a damage range it will never roll" is the
/// reason it now reads through the same resolver combat does.
/// </para>
/// <para>
/// The share is applied inside <c>ResolveAttackerStatsForHand</c> rather than by each caller, so
/// combat and this screen cannot disagree. These are the tests that would notice if that stopped
/// being true.
/// </para>
/// </remarks>
public sealed class OffHandShareReportingTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>A Temper wielding the same temper in both hands, at a level of the caller's choosing.</summary>
    private static (WorldHarness Harness, PlayerActor Actor) DualWielder(
        CharacterPath path,
        int level)
    {
        var harness = Loaded();
        var actor = harness.AddPlayer("Kaeda", West, path: path, level: level);

        // Ten to twenty in both hands, so the share is legible in the printed range rather than
        // being lost to rounding on a 1-2 weapon.
        var temper = harness.DefineWeapon(
            "temper", "a temper", ItemSlot.MainHand, delayPulses: 8, verb: "cut",
            damageMin: 10, damageMax: 20);

        harness.Equip(actor, temper, ItemSlot.MainHand);
        harness.Equip(actor, temper, ItemSlot.OffHand);

        return (harness, actor);
    }

    private static string Sheet(WorldHarness harness, PlayerActor actor)
    {
        harness.Drain(actor);
        harness.Execute(actor, "stats");
        return harness.DrainText(actor);
    }

    /// <summary>
    /// The reported range is the share, not the weapon. A Temper at the unlock has half of it.
    /// </summary>
    [Fact]
    public void The_sheet_reports_a_partly_trained_off_hand_at_its_share()
    {
        var (harness, actor) = DualWielder(CharacterPath.Temper, level: 3);

        var sheet = Sheet(harness, actor);

        // Half of 10-20, with the Might modifier of a level 3 Temper halved alongside it.
        Assert.Contains("Off Hand:", sheet, StringComparison.Ordinal);
        Assert.Contains("Damage Range: 5-10", sheet, StringComparison.Ordinal);

        // And the main hand is untouched by any of it.
        Assert.Contains("10-20", sheet, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a Temper at mastery has all of it, which is where the Path was before the ramp existed.
    /// </summary>
    [Fact]
    public void A_blade_at_mastery_swings_its_off_hand_whole()
    {
        var (harness, actor) = DualWielder(
            CharacterPath.Temper, AbilityProgression.OffHandMasteryLevel);

        var sheet = Sheet(harness, actor);

        var offHand = sheet[sheet.IndexOf("Off Hand:", StringComparison.Ordinal)..];
        Assert.Contains("Damage Range: 10-20", offHand, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Warden tops out at four fifths, because that hand is also holding a shield.
    /// </summary>
    [Fact]
    public void A_warden_at_mastery_tops_out_below_a_blade()
    {
        var (harness, actor) = DualWielder(
            CharacterPath.Warden, AbilityProgression.OffHandMasteryLevel);

        var sheet = Sheet(harness, actor);
        var offHand = sheet[sheet.IndexOf("Off Hand:", StringComparison.Ordinal)..];

        Assert.Contains("Damage Range: 8-16", offHand, StringComparison.Ordinal);
    }

    /// <summary>
    /// The screen and the fight read the same resolver, so the printed range is the rolled one.
    /// </summary>
    /// <remarks>
    /// Compared against the resolver directly rather than against a second copy of the arithmetic:
    /// a test that recomputed the share would agree with itself while the screen disagreed with
    /// combat, which is the failure it exists to catch.
    /// </remarks>
    [Theory]
    [InlineData(CharacterPath.Temper, 3)]
    [InlineData(CharacterPath.Temper, 20)]
    [InlineData(CharacterPath.Warden, 5)]
    [InlineData(CharacterPath.Warden, 30)]
    public void The_printed_range_is_the_one_combat_resolves(CharacterPath path, int level)
    {
        var (harness, actor) = DualWielder(path, level);

        var expected = Domain.Combat.EquipmentResolver.ResolveAttackerStatsForHand(
            actor.Character.Level,
            actor.Character.Attributes.MightModifier,
            harness.World.EquipmentOf(actor.CharacterId),
            ItemSlot.OffHand,
            AbilityProgression.OffHandDamageShare(path, level));

        var sheet = Sheet(harness, actor);
        var offHand = sheet[sheet.IndexOf("Off Hand:", StringComparison.Ordinal)..];

        Assert.Contains(
            $"Damage Range: {expected.MinDamage + expected.BaseDamage}-{expected.MaxDamage + expected.BaseDamage}",
            offHand,
            StringComparison.Ordinal);
    }
}
