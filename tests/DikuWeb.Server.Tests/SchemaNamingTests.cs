using System.Text.RegularExpressions;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DikuWeb.Server.Tests;

/// <summary>
/// The schema is snake_case everywhere (PLAN.md §6.1). These build the model and check it
/// directly rather than reading the migrations, so a new entity added without an explicit
/// column name fails here at the moment it is added - not when someone hand-writes a query
/// against it months later and finds they have to quote the identifier.
/// </summary>
public sealed class SchemaNamingTests
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>Model building needs a provider, not a reachable server.</summary>
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<DikuWebDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options;

        using var db = new DikuWebDbContext(options);
        return db.Model;
    }

    [Fact]
    public void Every_table_is_snake_case()
    {
        var offenders = BuildModel().GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => t is not null && !SnakeCase.IsMatch(t))
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_column_is_snake_case()
    {
        var offenders = new List<string>();

        foreach (var entity in BuildModel().GetEntityTypes())
        {
            if (StoreObjectIdentifier.Create(entity, StoreObjectType.Table) is not { } table)
            {
                continue;
            }

            offenders.AddRange(entity.GetProperties()
                .Select(p => p.GetColumnName(table))
                .Where(c => c is not null && !SnakeCase.IsMatch(c))
                .Select(c => $"{table.Name}.{c}"));
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_key_index_and_constraint_is_snake_case()
    {
        var offenders = new List<string>();

        foreach (var entity in BuildModel().GetEntityTypes())
        {
            offenders.AddRange(entity.GetKeys()
                .Select(k => k.GetName())
                .Concat(entity.GetForeignKeys().Select(f => f.GetConstraintName()))
                .Concat(entity.GetIndexes().Select(i => i.GetDatabaseName()))
                .Where(n => !string.IsNullOrEmpty(n) && !SnakeCase.IsMatch(n))!);
        }

        Assert.Empty(offenders.Distinct());
    }

    /// <summary>
    /// The convention fills gaps; it must not override a name a configuration chose.
    /// </summary>
    /// <remarks>
    /// This used to be asserted against the owned <c>vitals_*</c> columns on <c>mobs</c>, which
    /// was the only place in the model where an explicit name differed from what the convention
    /// would have produced. Dropping that table took the test's subject with it - and no
    /// production column distinguishes the two paths any more, so pointing it at one would have
    /// left an assertion that cannot fail.
    ///
    /// So it is asserted against the convention itself, over a model built here for the purpose.
    /// That is the more honest test in any case: <c>SnakeCaseNaming</c> is a rule about every
    /// entity, and tying the proof of it to one entity is what let it rot in the first place.
    /// </remarks>
    [Fact]
    public void An_explicit_column_name_survives_the_convention()
    {
        var model = BuildNamingProbe();
        var entity = model.GetEntityTypes().Single(e => e.ClrType == typeof(NamingProbe));
        var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value;

        string? Column(string property) =>
            entity.GetProperties().Single(p => p.Name == property).GetColumnName(table);

        // Chosen by hand, and deliberately not what the convention would produce.
        Assert.Equal("vitals_health_max", Column(nameof(NamingProbe.HealthCeiling)));

        // Left to the convention, which fills it in.
        Assert.Equal("wander_interval_pulses", Column(nameof(NamingProbe.WanderIntervalPulses)));

        // And the table name too, since it was never named explicitly.
        Assert.Equal("naming_probes", entity.GetTableName());
    }

    /// <summary>
    /// Built through a real context rather than a bare <c>ModelBuilder</c>, so the probe goes
    /// through the same provider conventions production entities do — which is the only way the
    /// result says anything about them.
    /// </summary>
    private static IModel BuildNamingProbe()
    {
        var options = new DbContextOptionsBuilder<NamingProbeContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options;

        using var db = new NamingProbeContext(options);
        return db.Model;
    }

    private sealed class NamingProbeContext(DbContextOptions<NamingProbeContext> options)
        : DbContext(options)
    {
        public DbSet<NamingProbe> NamingProbes => Set<NamingProbe>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NamingProbe>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.HealthCeiling).HasColumnName("vitals_health_max");
            });

            SnakeCaseNaming.ApplyTo(modelBuilder);
        }
    }

    private sealed class NamingProbe
    {
        public int Id { get; set; }

        public int HealthCeiling { get; set; }

        public int WanderIntervalPulses { get; set; }
    }
}
