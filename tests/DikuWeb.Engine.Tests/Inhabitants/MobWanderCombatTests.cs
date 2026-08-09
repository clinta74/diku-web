using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Inhabitants;

/// <summary>
/// A mob in a fight stays in the fight (PLAN.md §4.2).
/// </summary>
/// <remarks>
/// Wandering used to ignore combat entirely. Reported from live play: <em>"You begin attacking a
/// zombie!"</em>, two swings, then <em>"A zombie leaves north."</em> — the fight over before it had
/// started, and the player left standing in an empty room. Worse, until the departure fix landed
/// beside this one, the player was then stuck <c>Fighting</c> for the rest of the session.
///
/// It is the argument the stun guard already makes one level up, whose own comment says gating
/// only a mob's swings "would have it strolling out of the room mid-stun, which reads as the stun
/// having done nothing". A fight is the same claim on a mob's attention.
///
/// The state is asserted directly rather than through a real exchange: a fight long enough to
/// outlast several wander turns is a fight one side would win, and a test that has to keep both
/// combatants alive for two minutes is measuring the balance rather than the guard.
/// </remarks>
public sealed class MobWanderCombatTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>Sweeps to run — far more wander turns than any interval needs.</summary>
    private const int Sweeps = 40;

    private sealed class FakeMobTemplateRepository(MobTemplate template) : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template.Key == key ? template : null);

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>([template]);
    }

    private static MobTemplate Zombie() => new()
    {
        Key = "zombie",
        Name = "zombie",
        Icon = "z",
        Level = 1,
        WanderIntervalPulses = 4,
    };

    private static (WorldHarness Harness, Mob Mob, MobAiSystem Ai) Restless()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Zombie();

        var mob = new MobSpawner().Spawn(
            template, harness.World.FindZone("test.zone")!, harness.World.FindWorld("test")!, West);

        harness.World.AddMob(mob);

        var ai = new MobAiSystem(
            new FakeMobTemplateRepository(template),
            new SeededRandomSource(42),
            harness.Clock,
            harness.View);

        return (harness, mob, ai);
    }

    private static async Task<bool> WanderedAsync(WorldHarness harness, Mob mob, MobAiSystem ai)
    {
        for (var sweep = 0; sweep < Sweeps; sweep++)
        {
            await ai.RunAsync(harness.World, CancellationToken.None);
            harness.Pump(16);

            if (mob.RoomKey != West.ToString())
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public async Task A_mob_in_a_fight_does_not_wander_off()
    {
        var (harness, zombie, ai) = Restless();
        zombie.CombatState = CombatState.Fighting;

        Assert.False(
            await WanderedAsync(harness, zombie, ai),
            "A mob walked out of a fight it was in.");
    }

    [Fact]
    public async Task And_wanders_again_once_the_fight_is_over()
    {
        // The guard must not be a life sentence. A mob left permanently rooted because it was once
        // in a fight would be a quieter version of the same bug, and the sort that only shows up
        // as a zone that slowly stops moving.
        var (harness, zombie, ai) = Restless();
        zombie.CombatState = CombatState.Fighting;

        await WanderedAsync(harness, zombie, ai);
        zombie.CombatState = CombatState.Idle;

        Assert.True(
            await WanderedAsync(harness, zombie, ai),
            "A mob released from a fight never wandered again.");
    }

    [Fact]
    public async Task A_mob_that_is_not_fighting_is_unaffected()
    {
        // The case every other wander test relies on, asserted here too so a mistake in the guard
        // cannot pass by stopping everything.
        var (harness, zombie, ai) = Restless();

        Assert.True(
            await WanderedAsync(harness, zombie, ai),
            "An idle mob stopped wandering.");
    }
}
