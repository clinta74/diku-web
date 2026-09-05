using Muwbta.Persistence;
using Muwbta.Persistence.Seeding;
using Muwbta.Server.Building;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Muwbta.Server.Tests;

/// <summary>
/// The starter world's inhabitants, held to the same rules the Reaches' content files are.
/// </summary>
/// <remarks>
/// The seed is authored in C#, so nothing reads it through <c>check-bundle</c> on the way in.
/// Exporting it and validating the export is the same check, made on every run - and it is the
/// check that catches a quest naming a mob the seed does not plant, or a behavior key the engine
/// does not read, before a fresh server does.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class StarterContentTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_seeded_world_passes_the_bundle_validator()
    {
        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        var bundle = await new WorldExporter(db, TimeProvider.System)
            .ExportAsync(StarterWorldSeeder.WorldKey, null, CancellationToken.None);

        Assert.NotNull(bundle);

        var check = BundleValidator.Validate(bundle);

        Assert.True(check.Ok, string.Join(" | ", check.Findings.Select(f => $"{f.Level}: {f.Message}")));
    }

    [Fact]
    public async Task The_seed_plants_the_tavern()
    {
        using var scope = postgres.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuwbtaDbContext>();

        foreach (var mob in StarterWorldSeeder.MobTemplates)
        {
            Assert.True(await db.MobTemplates.AnyAsync(m => m.Key == mob.Key), mob.Key);
        }

        foreach (var spawner in StarterWorldSeeder.Spawners)
        {
            Assert.True(await db.Spawners.AnyAsync(s => s.Id == spawner.Id), spawner.TemplateKey);
        }

        Assert.True(await db.Quests.AnyAsync(q => q.Key == "millbrook-a-drink-for-the-old-man"));
    }
}
