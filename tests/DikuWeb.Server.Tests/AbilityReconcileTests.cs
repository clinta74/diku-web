using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;
using DikuWeb.Persistence.Seeding;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Tests;

/// <summary>
/// Seeding missing abilities from <see cref="AbilityCatalogue"/> - and, far more importantly,
/// leaving everything else alone.
/// </summary>
/// <remarks>
/// <b>Three of these tests assert the inverse of what they used to.</b> The reconcile made the
/// table match the catalogue exactly; abilities are builder-editable now, so a row that differs is
/// somebody's retune rather than staleness. The properties that matter are what survives a
/// restart: a purge and a rewrite each throw a builder's work away silently, and go on doing it on
/// every restart after that.
///
/// Against a real PostgreSQL on purpose - the whole subject is what is in the table across a
/// restart.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class AbilityReconcileTests(PostgresFixture postgres)
{
    private static Ability Row(string key, string name = "Old Name") => new()
    {
        Key = key,
        Path = CharacterPath.Warden,
        UnlockLevel = 1,
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
    public async Task An_ability_the_catalogue_does_not_define_is_left_alone()
    {
        // Was "is purged". A key the catalogue has never heard of used to be assumed stale - the
        // warden.slash rename. It now equally means an ability somebody authored, and this code
        // cannot tell those apart, so it must not guess: guessing wrong deletes content. Removing
        // an ability is a deliberate act through the builder.
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        db.Abilities.Add(Row("warden.improvised-headbutt"));
        await db.SaveChangesAsync();

        var result = await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        Assert.Equal(0, result.Removed);
        Assert.True(await db.Abilities.AnyAsync(a => a.Key == "warden.improvised-headbutt"));
    }

    [Fact]
    public async Task A_row_that_differs_from_the_catalogue_survives_a_restart()
    {
        // Was "is rewritten", and this is the load-bearing test of the whole change. A builder
        // retunes a cooldown; the next restart must not put the catalogue's number back. It did,
        // and it would have gone on doing it, so an edit would appear to save and then quietly
        // revert overnight - the worst shape a bug can take, because nothing reports it.
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        var target = AbilityCatalogue.All[0];

        var stored = await db.Abilities.FirstAsync(a => a.Key == target.Key);
        db.Abilities.Remove(stored);
        await db.SaveChangesAsync();
        db.Abilities.Add(Row(target.Key, "Retuned By A Builder"));
        await db.SaveChangesAsync();

        var result = await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Removed);

        var after = await db.Abilities.AsNoTracking().FirstAsync(a => a.Key == target.Key);
        Assert.Equal("Retuned By A Builder", after.Name);
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
    public async Task The_table_holds_the_catalogue_plus_whatever_was_authored()
    {
        // Was "exactly the catalogue". A superset now, and the superset is the point: the
        // catalogue is a floor the seeder guarantees, not a ceiling it enforces.
        await using var db = postgres.CreateDbContext();
        db.Abilities.Add(Row("hallow.authored-here"));
        await db.SaveChangesAsync();

        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        var stored = await db.Abilities.AsNoTracking().Select(a => a.Key).ToListAsync();

        foreach (var entry in AbilityCatalogue.All)
        {
            Assert.Contains(entry.Key, stored);
        }

        Assert.Contains("hallow.authored-here", stored);
    }

    [Fact]
    public async Task Path_and_unlock_level_are_seeded_rather_than_defaulted()
    {
        // The two new columns carry who learns an ability and when. Left to default to zero they
        // would make every ability a Warden one known from level 1 - which is exactly what the
        // scaffolded migration would have produced for every existing deployment.
        await using var db = postgres.CreateDbContext();
        await StarterWorldSeeder.ReconcileAbilitiesAsync(db);

        var bolt = await db.Abilities.AsNoTracking().FirstAsync(a => a.Key == "adept.bolt");
        var capstone = await db.Abilities.AsNoTracking()
            .FirstAsync(a => a.Key == "hallow.intercession");

        Assert.Equal(CharacterPath.Adept, bolt.Path);
        Assert.Equal(1, bolt.UnlockLevel);
        Assert.Equal(CharacterPath.Hallow, capstone.Path);
        Assert.Equal(20, capstone.UnlockLevel);
    }
}
