using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DikuWeb.Persistence;

/// <summary>
/// Forces every database identifier EF generates to snake_case.
/// </summary>
/// <remarks>
/// EF names tables and columns after CLR members, so an entity added without an explicit
/// <c>HasColumnName</c> silently lands as <c>ItemInstances.TemplateKey</c> next to
/// <c>rooms.zone_key</c>. Postgres then requires quoting for one and not the other, which is
/// the kind of inconsistency that is only ever discovered by hand-writing a query.
///
/// This fills in the gaps rather than overriding: anything a configuration named explicitly
/// keeps that name, so deliberate choices like the owned <c>vitals_*</c> columns survive.
/// </remarks>
internal static class SnakeCaseNaming
{
    public static void ApplyTo(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Owned types share their owner's table; renaming it here would fight the owner.
            if (entity.GetTableName() is { } table && !entity.IsOwned())
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            var storeObject = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);

            foreach (var property in entity.GetProperties())
            {
                if (!HasExplicitName(property) &&
                    storeObject is { } target &&
                    property.GetColumnName(target) is { } column)
                {
                    property.SetColumnName(ToSnakeCase(column));
                }
            }

            // Owned types report their owner's key and index names, so setting them here would
            // rename the same constraint twice. Empty names are skipped rather than passed
            // through: EF rejects an empty identifier outright.
            foreach (var key in entity.GetKeys())
            {
                if (key.GetName() is { Length: > 0 } name)
                {
                    key.SetName(ToSnakeCase(name));
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                if (foreignKey.GetConstraintName() is { Length: > 0 } name)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(name));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                if (index.GetDatabaseName() is { Length: > 0 } name)
                {
                    index.SetDatabaseName(ToSnakeCase(name));
                }
            }
        }
    }

    /// <summary>
    /// Whether a configuration set the column name by hand, in which case it wins. Reading the
    /// annotation rather than <c>GetColumnName()</c> is the point: the latter always returns a
    /// name, so it cannot distinguish "chosen" from "defaulted".
    /// </summary>
    private static bool HasExplicitName(IMutableProperty property) =>
        property.FindAnnotation(RelationalAnnotationNames.ColumnName) is not null;

    /// <summary>
    /// "ItemInstances" → "item_instances", "IX_Mobs_RoomKey" → "ix_mobs_room_key".
    /// Runs of capitals stay together, so "BaseXp" is "base_xp" and not "base_x_p".
    /// </summary>
    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (c == '_')
            {
                // Already-separated names ("IX_Mobs_RoomKey") must not gain a second underscore.
                if (builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                continue;
            }

            if (char.IsUpper(c) && builder.Length > 0 && builder[^1] != '_' &&
                (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
