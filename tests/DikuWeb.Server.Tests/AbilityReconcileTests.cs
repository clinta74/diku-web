using DikuWeb.Domain.Abilities;
using DikuWeb.Persistence.Seeding;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Reconciling the abilities table against <see cref="AbilityCatalogue"/>.
/// </summary>
/// <remarks>
/// Against a real PostgreSQL on purpose. A replaced row is deleted and re-inserted under the same
/// primary key, which is the kind of thing that works against a fake and violates a unique
/// constraint against the real one.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class AbilityReconcileTests(PostgresFixture postgres)
{
    private static Ability Row(string key, string name = "Old Name") => new()
    {
        Key = key,
        Name = name,
        Description = "Left over from an older build.",
        CostType = CostType.Stamina,
        CostValue = 1,
        CooldownPulses = 1,
        CastTimePulses = null,
        TargetingType = TargetingType.Self,
        EffectKey = "heal.restore",
        EffectParams = new Dictionary<string, string>(StringComparer.Ordinal) { ["baseHeal"] = "1" },
    };

    [Fact]
    public async Task Every_catalogue_ability_ends_up_in_the_table()
    {
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        var stored = await db.Abilities.AsNoTracking()
            .Select(a => a.Key)
            .ToListAsync();

        foreach (var entry in AbilityCatalogue.All)
        {
            Assert.Contains(entry.Key, stored);
        }
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        // The reconcile runs on every startup, so it has to be a no-op once it agrees. A version
        // that rewrote rows regardless would churn the table on every boot.
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        var second = await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        Assert.False(second.ChangedAnything, $"Second run: {second}.");
    }

    [Fact]
    public async Task An_ability_the_catalogue_no_longer_defines_is_purged()
    {
        // The case that prompted this: warden.slash and warden.parry became warden.kick and a
        // passive, and the old rows would otherwise sit in the table forever, castable by nobody.
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        db.Abilities.Add(Row("warden.slash"));
        await db.SaveChangesAsync();

        var result = await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        Assert.Equal(1, result.Removed);
        Assert.False(await db.Abilities.AnyAsync(a => a.Key == "warden.slash"));
    }

    [Fact]
    public async Task A_row_that_has_drifted_from_the_catalogue_is_rewritten()
    {
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        var target = AbilityCatalogue.All[0];

        // Tamper with a real row, the way an older build's values would look.
        var stored = await db.Abilities.FirstAsync(a => a.Key == target.Key);
        db.Abilities.Remove(stored);
        await db.SaveChangesAsync();
        db.Abilities.Add(Row(target.Key, "Stale Name"));
        await db.SaveChangesAsync();

        var result = await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Removed);

        var after = await db.Abilities.AsNoTracking().FirstAsync(a => a.Key == target.Key);
        Assert.Equal(target.Name, after.Name);
        Assert.Equal(target.CostValue, after.CostValue);
        Assert.Equal(target.EffectKey, after.EffectKey);
    }

    [Fact]
    public async Task A_missing_ability_is_restored()
    {
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        var target = AbilityCatalogue.All[^1];
        db.Abilities.Remove(await db.Abilities.FirstAsync(a => a.Key == target.Key));
        await db.SaveChangesAsync();

        var result = await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        Assert.Equal(1, result.Added);
        Assert.True(await db.Abilities.AnyAsync(a => a.Key == target.Key));
    }

    [Fact]
    public async Task The_table_ends_up_holding_exactly_the_catalogue()
    {
        // Belt and braces over the three counters: whatever the starting state, the table after a
        // reconcile is the catalogue and nothing else.
        await using var db = postgres.CreateDbContext();
        db.Abilities.Add(Row("ghost.ability"));
        await db.SaveChangesAsync();

        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        var stored = await db.Abilities.AsNoTracking().Select(a => a.Key).ToListAsync();

        Assert.Equal(
            AbilityCatalogue.All.Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal),
            stored.OrderBy(k => k, StringComparer.Ordinal));
    }
}
