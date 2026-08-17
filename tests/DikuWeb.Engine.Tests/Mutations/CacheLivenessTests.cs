using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Spawning;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Quests;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DikuWeb.Engine.Tests.Mutations;

/// <summary>
/// A builder's save must reach the running world, not just the database (PLAN.md §1, "live
/// immediate"). Templates and quests live in side caches that were previously loaded once at
/// startup and never updated, so an edit changed what spawned (the sweep reads repositories) but
/// not what a shopkeeper charged or what an NPC said (those read these caches).
/// </summary>
public sealed class CacheLivenessTests
{
    private static UpsertQuest NewQuest(string key, string giver, string name = "A Quest") =>
        new(key, "test.zone", name, "", "", giver, giver, null, 1, 0, 0, null, 1, null, [], false, false, [], 0);

    private static (WorldMutationApplier Applier, QuestCache Quests, MobTemplateCache Mobs, ItemTemplateCache Items, SpawnerCache Spawners) NewApplier()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var quests = new QuestCache();
        var mobs = new MobTemplateCache();
        var items = new ItemTemplateCache();
        var spawners = new SpawnerCache();

        var applier = new WorldMutationApplier(
            harness.World, harness.View, harness.Options, quests, mobs, items, spawners);

        return (applier, quests, mobs, items, spawners);
    }

    [Fact]
    public void Saving_a_quest_makes_it_live_without_a_reload()
    {
        var (applier, quests, _, _, _) = NewApplier();

        applier.Apply(NewQuest("test.errand", "guard"));

        Assert.NotNull(quests.Get("test.errand"));
        Assert.Contains(quests.GetByGiverMobKey("guard"), q => q.Key == "test.errand");
    }

    [Fact]
    public void Repointing_a_quest_at_a_new_giver_clears_the_old_index()
    {
        // The giver index is what `talk` reads. A stale entry would leave the previous NPC still
        // offering a quest they no longer own.
        var (applier, quests, _, _, _) = NewApplier();

        applier.Apply(NewQuest("test.errand", "guard"));
        applier.Apply(NewQuest("test.errand", "captain"));

        Assert.Empty(quests.GetByGiverMobKey("guard"));
        Assert.Contains(quests.GetByTurninMobKey("captain"), q => q.Key == "test.errand");
        Assert.Single(quests.All);
    }

    [Fact]
    public void Deleting_a_quest_drops_it_from_the_cache_and_its_indexes()
    {
        var (applier, quests, _, _, _) = NewApplier();

        applier.Apply(NewQuest("test.errand", "guard"));
        applier.Apply(new DeleteQuest("test.errand"));

        Assert.Null(quests.Get("test.errand"));
        Assert.Empty(quests.GetByGiverMobKey("guard"));
    }

    [Fact]
    public void Saving_a_mob_template_makes_it_live()
    {
        var (applier, _, mobs, _, _) = NewApplier();

        applier.Apply(new UpsertMobTemplate(
            "rat", "a rat", "", "r", 1, 24, [], 0, 0, [], [], []));

        Assert.Equal("a rat", mobs.Get("rat")?.Name);

        applier.Apply(new UpsertMobTemplate(
            "rat", "a dire rat", "", "r", 3, 24, [], 0, 0, [], [],
            [new MobAttack { Verb = "bite", DelayPulses = 6 }]));

        Assert.Equal("a dire rat", mobs.Get("rat")?.Name);
        Assert.Equal(3, mobs.Get("rat")?.Level);

        // Combat reads attacks from this cache, so an edit that stopped here would persist a new
        // cadence the running fight never saw.
        var attack = Assert.Single(mobs.Get("rat")?.Attacks ?? []);
        Assert.Equal("bite", attack.Verb);
        Assert.Equal(6, attack.DelayPulses);

        applier.Apply(new DeleteMobTemplate("rat"));
        Assert.Null(mobs.Get("rat"));
    }

    [Fact]
    public void Saving_an_item_template_makes_it_live()
    {
        var (applier, _, _, items, _) = NewApplier();

        applier.Apply(new UpsertItemTemplate(
            "blade", "a blade", "", "/", ItemSlot.MainHand, 10, 25, [], 8, "slash", false, false, false, false, []));

        Assert.Equal(25, items.Get("blade")?.BaseValue);
        Assert.Equal(8, items.Get("blade")?.AttackDelayPulses);
        Assert.Equal("slash", items.Get("blade")?.AttackVerb);

        // The shop price is the reason this matters: it reads the cache, not the repository.
        // So does weapon speed - combat resolves the delay through this same cache every pulse.
        // The quest-item flag rides along: the spawner reads it from this cache too.
        applier.Apply(new UpsertItemTemplate(
            "blade", "a blade", "", "/", ItemSlot.MainHand, 10, 99, [], 4, "cleave", true, true, true, true,
            [CharacterPath.Warden]));

        // The three restrictions ride along too - they are read from this cache at pick-up,
        // at the shop counter and at the moment of equipping.
        Assert.True(items.Get("blade")?.IsLore);
        Assert.True(items.Get("blade")?.IsNoDrop);
        Assert.Equal([CharacterPath.Warden], items.Get("blade")?.Paths);

        // And so does the light, which is read from this cache every time a dark room is drawn.
        Assert.True(items.Get("blade")?.IsLightSource);

        Assert.Equal(99, items.Get("blade")?.BaseValue);
        Assert.Equal(4, items.Get("blade")?.AttackDelayPulses);
        Assert.Equal("cleave", items.Get("blade")?.AttackVerb);
        Assert.True(items.Get("blade")?.IsQuestItem);

        applier.Apply(new DeleteItemTemplate("blade"));
        Assert.Null(items.Get("blade"));
    }

    [Fact]
    public void Saving_a_spawner_makes_it_live()
    {
        // The sweep reads this cache rather than the spawners table, so a save that stopped at
        // the database would leave the rule dormant until the next restart.
        var (applier, _, _, _, spawners) = NewApplier();

        var id = Guid.CreateVersion7();
        applier.Apply(new UpsertSpawner(
            id, "test.zone", "rat", TemplateKind.Mob, ["test.zone.west"], 2, null, null));

        Assert.Equal(2, spawners.Get(id)?.TargetCount);

        applier.Apply(new UpsertSpawner(
            id, "test.zone", "rat", TemplateKind.Mob, ["test.zone.west", "test.zone.east"], 5, true, null));

        Assert.Equal(5, spawners.Get(id)?.TargetCount);
        Assert.Equal(2, spawners.Get(id)?.RoomKeys.Count);
        Assert.True(spawners.Get(id)?.Wanders);
        Assert.Single(spawners.All);

        applier.Apply(new DeleteSpawner(id));
        Assert.Null(spawners.Get(id));
        Assert.Empty(spawners.All);
    }

    [Fact]
    public void An_applier_without_caches_still_applies_cleanly()
    {
        // The caches are optional so tests and trimmed hosts can build an applier without them.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var applier = new WorldMutationApplier(harness.World, harness.View, harness.Options);

        var result = applier.Apply(NewQuest("test.errand", "guard"));

        Assert.True(result.Success);
    }

    [Fact]
    public void The_container_supplies_the_caches_so_edits_are_not_silently_dropped()
    {
        // The caches are optional constructor parameters. If the container ever stopped supplying
        // them, every builder save would quietly go back to needing a restart and no other test
        // here would notice - the applier would still report success.
        var services = new ServiceCollection();
        services.AddSingleton(new EngineOptions());
        services.AddSingleton<DikuWeb.Domain.Randomness.IRandomSource>(
            new DikuWeb.Domain.Randomness.SeededRandomSource(1));
        services.AddSingleton<DikuWeb.Engine.World.WorldState>();
        services.AddSingleton<RoomLayoutService>();
        services.AddSingleton<PlayerView>();
        services.AddSingleton<QuestCache>();
        services.AddSingleton<MobTemplateCache>();
        services.AddSingleton<ItemTemplateCache>();
        services.AddSingleton<SpawnerCache>();
        services.AddSingleton<WorldMutationApplier>();

        using var provider = services.BuildServiceProvider();
        var applier = provider.GetRequiredService<WorldMutationApplier>();

        applier.Apply(NewQuest("test.errand", "guard"));

        var spawnerId = Guid.CreateVersion7();
        applier.Apply(new UpsertSpawner(
            spawnerId, "test.zone", "rat", TemplateKind.Mob, ["test.zone.west"], 1, false, null));

        Assert.NotNull(provider.GetRequiredService<QuestCache>().Get("test.errand"));
        Assert.NotNull(provider.GetRequiredService<SpawnerCache>().Get(spawnerId));
    }
}
