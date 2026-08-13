using DikuWeb.Domain.Abilities;
using DikuWeb.Persistence.Migrations;
using DikuWeb.Persistence.Seeding;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Tests;

/// <summary>
/// The <c>AbilityPathAndUnlockLevel</c> migration's backfill, run against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <b>This exists because no ordinary test run touches the statement it tests.</b> Testcontainers
/// builds an empty database, applies every migration to it, and then seeds — so the two columns are
/// added to a table with no rows in it, the backfill updates nothing, and the seeder inserts rows
/// that already carry a path. Every assertion elsewhere in the suite would pass with the UPDATE
/// deleted entirely.
///
/// The deployments that *do* run it are the ones that already hold all thirty-seven rows, which is
/// every server that has booted since 5.1e. Getting it wrong there makes every ability a Warden
/// ability known from level 1 — a silent, total break of the progression table on somebody else's
/// database, discovered by a player rather than by a test.
///
/// So the pre-migration state is reconstructed rather than waited for: zero the columns, run the
/// real statement (the migration's own constant, not a copy of it), and check what comes out.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class AbilityBackfillTests(PostgresFixture postgres)
{
    [Fact]
    public async Task It_restores_every_path_and_unlock_level_the_catalogue_ships()
    {
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        // The state an upgrading deployment is in the instant after AddColumn: rows present,
        // both new columns at their zero default.
        await db.Database.ExecuteSqlRawAsync("UPDATE abilities SET path = 0, unlock_level = 0;");

        await db.Database.ExecuteSqlRawAsync(AbilityPathAndUnlockLevel.BackfillSql);

        var stored = await db.Abilities.AsNoTracking()
            .ToDictionaryAsync(a => a.Key, StringComparer.Ordinal);

        foreach (var entry in AbilityCatalogue.All)
        {
            var row = Assert.Contains(entry.Key, stored);

            Assert.True(
                row.Path == entry.Path,
                $"{entry.Key}: backfilled path {row.Path}, catalogue says {entry.Path}.");

            Assert.True(
                row.UnlockLevel == entry.UnlockLevel,
                $"{entry.Key}: backfilled level {row.UnlockLevel}, catalogue says {entry.UnlockLevel}.");
        }
    }

    [Fact]
    public async Task It_leaves_no_ability_at_the_zero_default()
    {
        // The failure this migration exists to prevent, stated as its own assertion rather than
        // inferred from the one above: an unlock level of 0 is known by everyone from level 1, and
        // a path of 0 is Warden. A key the VALUES list forgot would land in exactly that state, and
        // comparing key-by-key against the catalogue would not notice a *missing* pair.
        //
        // Scoped to catalogue keys, and that is not a loosening. The suite shares one database, so
        // rows authored by other tests are present here - and a row the backfill does not name is
        // the correct outcome for those, since the backfill only ever claimed to answer for what
        // the catalogue shipped. Asserting over the whole table made this pass alone and fail in a
        // full run, which is a statement about the fixture rather than about the migration.
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        await db.Database.ExecuteSqlRawAsync("UPDATE abilities SET path = 0, unlock_level = 0;");
        await db.Database.ExecuteSqlRawAsync(AbilityPathAndUnlockLevel.BackfillSql);

        var shipped = AbilityCatalogue.All.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        var unfilled = (await db.Abilities.AsNoTracking()
                .Where(a => a.UnlockLevel == 0)
                .Select(a => a.Key)
                .ToListAsync())
            .Where(shipped.Contains)
            .ToList();

        Assert.True(unfilled.Count == 0, $"Not named by the backfill: {string.Join(", ", unfilled)}.");
    }
}
