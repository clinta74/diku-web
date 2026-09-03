using Muwbta.Domain.Combat;
using Muwbta.Domain.Entities;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Randomness;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Systems;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Inhabitants;

/// <summary>
/// Who an aggressive mob jumps when it starts the fight itself (BUGS.md #20).
/// </summary>
/// <remarks>
/// <para>
/// This was <c>occupants.FirstOrDefault()</c> over a list in strict arrival order. Three things
/// followed, in severity order: a <b>link-dead</b> character stays in the room for the whole grace
/// window and, having stood there longest, soaked every aggressive mob in it and could be killed
/// while offline; every mob in a room picked the same person; and the opening target was decided by
/// something no player could see or plan around.
/// </para>
/// <para>
/// What was <em>not</em> broken is tanking, which #20 claimed was unimplementable. A hate list is a
/// cumulative damage meter and <c>CombatSystem</c> re-reads <c>GetTopHater</c> every round, so both
/// a taunt and simply out-damaging everyone already pull an engaged mob — see <c>TauntTests</c>.
/// Only the <em>opening</em> target was out of reach, and the case that mattered was an add walking
/// into a fight and ignoring the tank holding it.
/// </para>
/// </remarks>
public sealed class AggressionTargetTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>
    /// Never reached: <see cref="WorldHarness.AddMob"/> puts the template in the cache, which marks
    /// it loaded, and the AI reads the cache in preference to the repository. Throwing rather than
    /// returning null keeps that honest — if the cache path ever stops being taken these tests say
    /// so instead of quietly finding no template and doing nothing at all.
    /// </summary>
    private sealed class UnusedMobTemplateRepository : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            throw new InvalidOperationException("The AI should have read the template cache.");

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            throw new InvalidOperationException("The AI should have read the template cache.");
    }

    private static Dictionary<string, object> Aggressive() =>
        WorldHarness.AsPersisted(new Dictionary<string, object> { ["type"] = "aggressive" });

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static MobAiSystem AiFor(WorldHarness harness, int seed = 42) =>
        new(
            new UnusedMobTemplateRepository(),
            new SeededRandomSource(seed),
            harness.Clock,
            harness.View,
            harness.MobTemplates);

    private static Mob AggressiveMob(WorldHarness harness, string key = "wolf") =>
        harness.AddMob(
            key,
            West,
            attacks: [new MobAttack { Verb = "bite", DelayPulses = 4 }],
            name: "a wolf",
            behavior: Aggressive(),
            health: 100_000,
            damageMin: 5,
            damageMax: 5);

    private static string? TargetOf(Mob mob) => mob.CurrentTarget;

    private static string Id(PlayerActor actor) => EntityId.ForCharacter(actor.CharacterId);

    // -----------------------------------------------------------------------
    // Link-dead bodies
    // -----------------------------------------------------------------------

    /// <summary>
    /// The sharp end of #20: a disconnected player was always the one who had stood there longest.
    /// </summary>
    [Fact]
    public async Task A_link_dead_body_is_not_the_one_a_mob_jumps()
    {
        var harness = Loaded();

        // Added first, so arrival order would name them - which is the whole of the old rule.
        var dropped = harness.AddPlayer("Ilse", West);
        dropped.Output = null;
        Assert.True(dropped.IsLinkDead);

        var present = harness.AddPlayer("Theron", West);
        var wolf = AggressiveMob(harness);

        await AiFor(harness).RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(Id(present), TargetOf(wolf));
        Assert.Equal(CombatState.Idle, dropped.Character.CombatState);
    }

    /// <summary>
    /// A room holding nothing but dropped connections is an empty room as far as aggression goes.
    /// Without this the mob engages a body and then swings at it for the whole grace window.
    /// </summary>
    [Fact]
    public async Task A_room_of_nothing_but_link_dead_bodies_starts_no_fight()
    {
        var harness = Loaded();

        var dropped = harness.AddPlayer("Ilse", West);
        dropped.Output = null;

        var wolf = AggressiveMob(harness);

        await AiFor(harness).RunAsync(harness.World, CancellationToken.None);

        Assert.Null(TargetOf(wolf));
        Assert.Equal(CombatState.Idle, wolf.CombatState);
        Assert.Null(harness.World.FindCombat(West));
    }

    /// <summary>
    /// Deliberately <b>not</b> fixed, and asserted so nobody "tidies" it into a fix.
    /// </summary>
    /// <remarks>
    /// Dropping out is not a way to leave a fight. The eligibility rule governs the opening target
    /// only; a player whose connection fails mid-fight keeps being swung at, and may well die and
    /// rebind. The alternative — mobs disengaging on a lost connection — makes pulling the plug the
    /// safest escape in the game.
    /// </remarks>
    [Fact]
    public void Dropping_out_mid_fight_does_not_call_the_mob_off()
    {
        var harness = Loaded();

        var player = harness.AddPlayer("Theron", West);
        var wolf = AggressiveMob(harness);

        harness.Execute(player, "attack wolf");
        harness.Drain(player);

        player.Output = null;
        var before = player.Character.Vitals.Health;

        harness.Pump(12);

        Assert.True(player.Character.Vitals.Health < before);
        Assert.Equal(Id(player), harness.World.FindCombat(West)?.GetTopHater(EntityId.ForMob(wolf.Id)));
    }

    // -----------------------------------------------------------------------
    // The opening pick
    // -----------------------------------------------------------------------

    /// <summary>
    /// An add joins the fight on whoever is already holding the room — the case tanking turns on.
    /// </summary>
    [Fact]
    public async Task An_add_joins_the_fight_on_whoever_is_holding_the_room()
    {
        var harness = Loaded();

        // Added first, so arrival order would name the caster rather than the tank.
        var caster = harness.AddPlayer("Ilse", West, path: Domain.Characters.CharacterPath.Adept);
        var tank = harness.AddPlayer("Theron", West);

        var held = AggressiveMob(harness, "wolf");
        harness.Execute(tank, "attack wolf");
        harness.World.FindCombat(West)!.AddToHateList(
            EntityId.ForMob(held.Id), Id(tank), 500);

        var add = AggressiveMob(harness, "hound");

        await AiFor(harness).RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(Id(tank), TargetOf(add));
    }

    /// <summary>
    /// And it follows the threat rather than the person, so taunting the add off the caster works
    /// the same way as holding it from the start.
    /// </summary>
    [Fact]
    public async Task The_leader_is_whoever_holds_the_most_threat_not_whoever_arrived_first()
    {
        var harness = Loaded();

        var first = harness.AddPlayer("Ilse", West, path: Domain.Characters.CharacterPath.Adept);
        var second = harness.AddPlayer("Theron", West);

        var held = AggressiveMob(harness, "wolf");
        harness.Execute(first, "attack wolf");

        var combat = harness.World.FindCombat(West)!;
        var wolfId = EntityId.ForMob(held.Id);
        combat.AddToHateList(wolfId, Id(first), 100);
        combat.AddToHateList(wolfId, Id(second), 400);

        var add = AggressiveMob(harness, "hound");

        await AiFor(harness).RunAsync(harness.World, CancellationToken.None);

        Assert.Equal(Id(second), TargetOf(add));
    }

    /// <summary>
    /// The floor sits at the opening seed, not at zero.
    /// </summary>
    /// <remarks>
    /// Engaging seeds <see cref="CombatEngagement.OpeningThreat"/> so a mob has someone to swing
    /// at. Read as earned threat, that would make the first person a mob rolled onto the room's
    /// leader and send every other mob after them — the deterministic pile-on again, with a random
    /// first step. Both halves are asserted: a bare seed does not lead, and one point above it does.
    /// </remarks>
    [Theory]
    [InlineData(CombatEngagement.OpeningThreat, false)]
    [InlineData(CombatEngagement.OpeningThreat + 1, true)]
    public async Task Only_threat_above_the_opening_seed_makes_someone_the_leader(
        int threat,
        bool expectedToLead)
    {
        var picks = new HashSet<string?>();

        // Across seeds, because "not forced" is the claim - one seed could agree by luck.
        for (var seed = 0; seed < 12; seed++)
        {
            var harness = Loaded();

            var quiet = harness.AddPlayer("Ilse", West, path: Domain.Characters.CharacterPath.Adept);
            var threatened = harness.AddPlayer("Theron", West);

            var held = AggressiveMob(harness, "wolf");
            harness.Execute(quiet, "attack wolf");
            harness.World.FindCombat(West)!.AddToHateList(
                EntityId.ForMob(held.Id), Id(threatened), threat);

            var add = AggressiveMob(harness, "hound");

            await AiFor(harness, seed).RunAsync(harness.World, CancellationToken.None);

            picks.Add(TargetOf(add) == Id(threatened) ? "threatened" : "someone else");
        }

        if (expectedToLead)
        {
            Assert.Equal(["threatened"], picks);
        }
        else
        {
            Assert.Contains("someone else", picks);
        }
    }

    /// <summary>
    /// A mob starting a fight in a quiet room picks at random, so the pile-on is gone: nobody has
    /// earned its attention, and arrival order was never legible to the player anyway.
    /// </summary>
    [Fact]
    public async Task An_unprovoked_mob_does_not_always_pick_the_same_player()
    {
        var picked = new HashSet<string>();

        for (var seed = 0; seed < 12; seed++)
        {
            var harness = Loaded();

            var ilse = harness.AddPlayer("Ilse", West);
            harness.AddPlayer("Theron", West);

            var wolf = AggressiveMob(harness);

            await AiFor(harness, seed).RunAsync(harness.World, CancellationToken.None);

            picked.Add(TargetOf(wolf) == Id(ilse) ? "Ilse" : "Theron");
        }

        Assert.Equal(2, picked.Count);
    }

    /// <summary>
    /// Whoever it picks, it opens the fight properly — and does not swing the victim's own weapon
    /// round at it. Being jumped is not a target you chose.
    /// </summary>
    [Fact]
    public async Task Being_jumped_engages_the_mob_but_leaves_the_player_untargeted()
    {
        var harness = Loaded();

        var player = harness.AddPlayer("Theron", West);
        var wolf = AggressiveMob(harness);

        await AiFor(harness).RunAsync(harness.World, CancellationToken.None);

        var combat = harness.World.FindCombat(West)!;
        var wolfId = EntityId.ForMob(wolf.Id);

        Assert.Equal(CombatState.Fighting, wolf.CombatState);
        Assert.Equal(CombatState.Fighting, player.Character.CombatState);
        Assert.Equal(Id(player), combat.GetTopHater(wolfId));

        // You still have to fight back on purpose.
        Assert.Null(player.Character.CurrentTarget);
        Assert.False(combat.PlayerTargets.ContainsKey(player.CharacterId));
    }
}
