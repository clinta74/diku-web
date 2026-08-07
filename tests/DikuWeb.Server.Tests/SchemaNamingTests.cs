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

    [Fact]
    public void The_owned_vitals_columns_keep_their_explicit_names()
    {
        // The convention fills gaps; it must not override a name a configuration chose. Vitals
        // is the case that proves it - the owned property is "Health", and left to the
        // convention it would collide with any other "health" column on the same table.
        // Vitals is owned, so it reports the same table as its owner - hence the IsOwned filter.
        var mob = BuildModel().GetEntityTypes()
            .Single(e => e.GetTableName() == "mobs" && !e.IsOwned());
        var vitals = mob.GetNavigations().Single(n => n.Name == "Vitals").TargetEntityType;
        var table = StoreObjectIdentifier.Create(mob, StoreObjectType.Table)!.Value;

        var columns = vitals.GetProperties().Select(p => p.GetColumnName(table)).ToList();

        Assert.Contains("vitals_health", columns);
        Assert.Contains("vitals_health_max", columns);
    }
}
