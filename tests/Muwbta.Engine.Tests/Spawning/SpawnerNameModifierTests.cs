using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Muwbta.Engine.Tests.Spawning;

/// <summary>
/// A spawner naming the mobs it places, with one word after the article (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// <b>Asserted through a real sweep</b>, for the reason <see cref="SpawnerLevelOverrideTests"/>
/// gives: the composition is one call, and testing the call would pass whether or not the sweep
/// made it. What matters is that the mob standing in the room carries the word, that the template
/// row does not, and that the verbs which find a mob by name still find it.
/// </remarks>
public sealed class SpawnerNameModifierTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private sealed class FakeSpawnerRepository(params Spawner[] spawners) : ISpawnerRepository
    {
        public Task<IReadOnlyList<Spawner>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Spawner>>(spawners);
    }

    private sealed class FakeMobTemplateRepository(MobTemplate template) : IMobTemplateRepository
    {
        public Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(template.Key == key ? template : null);

        public Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MobTemplate>>([template]);
    }

    private sealed class FakeItemTemplateRepository : IItemTemplateRepository
    {
        public Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult<ItemTemplate?>(null);

        public Task<IReadOnlyList<ItemTemplate>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ItemTemplate>>([]);
    }

    private static MobTemplate Template(string name) => new()
    {
        Key = "brigand",
        Name = name,
        Icon = "t",
        Level = 7,
        BaseStats = WorldHarness.AsPersisted(new Dictionary<string, object>
        {
            ["health"] = 40,
            ["damage"] = "4-7",
        }),
    };

    private static async Task<(WorldHarness Harness, Mob Mob, MobTemplate Template)> SweptAsync(
        string templateName, string? modifier)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var template = Template(templateName);
        var spawner = new Spawner
        {
            ZoneKey = "test.zone",
            TemplateKey = template.Key,
            TemplateKind = TemplateKind.Mob,
            RoomKeys = [West.ToString()],
            TargetCount = 1,
            NameModifier = modifier,
        };

        var system = new SpawnerSystem(
            new FakeSpawnerRepository(spawner),
            new FakeMobTemplateRepository(template),
            new FakeItemTemplateRepository(),
            new MobSpawner(),
            new ItemSpawner(),
            NullLogger<SpawnerSystem>.Instance,
            harness.View);

        await system.RunAsync(harness.World, pulse: 0, CancellationToken.None);

        return (harness, Assert.Single(harness.World.MobsIn(West)), template);
    }

    [Fact]
    public async Task The_word_goes_after_the_article_and_the_template_keeps_its_own_name()
    {
        var (_, mob, template) = await SweptAsync("a brigand", "marsh");

        Assert.Equal("a marsh brigand", mob.DisplayName);
        Assert.Equal("a brigand", template.Name);
        Assert.Equal("brigand", mob.TemplateKey);
    }

    [Fact]
    public async Task No_modifier_is_what_every_existing_spawner_has()
    {
        var (_, mob, _) = await SweptAsync("a brigand", null);

        Assert.Equal("a brigand", mob.DisplayName);
    }

    [Fact]
    public async Task The_modified_mob_still_answers_to_the_kind_and_to_the_word()
    {
        // The reason nothing downstream needs to know a modifier exists: matching is derived
        // from the display name, so `attack brigand` and `attack marsh` both land.
        var (harness, mob, _) = await SweptAsync("a brigand", "marsh");
        var room = harness.World.MobsIn(West);

        Assert.Same(mob, NameMatch.Best(room, "brigand", m => m.TemplateName, m => m.TemplateKey));
        Assert.Same(mob, NameMatch.Best(room, "marsh", m => m.TemplateName, m => m.TemplateKey));
        Assert.Same(mob, NameMatch.Best(room, "a marsh brigand", m => m.TemplateName, m => m.TemplateKey));
    }

    [Fact]
    public async Task A_named_character_keeps_their_name_whatever_the_spawner_says()
    {
        // The API refuses this; an import does not. A stored word on a person has to be inert
        // rather than produce "Tessa marsh Roke" in every room listing.
        var (_, mob, _) = await SweptAsync("Tessa Roke, armourer", "marsh");

        Assert.Equal("Tessa Roke, armourer", mob.DisplayName);
    }

    [Fact]
    public async Task The_article_follows_the_word_not_the_noun()
    {
        var (_, mob, _) = await SweptAsync("an engine", "hall");

        Assert.Equal("a hall engine", mob.DisplayName);
    }
}
