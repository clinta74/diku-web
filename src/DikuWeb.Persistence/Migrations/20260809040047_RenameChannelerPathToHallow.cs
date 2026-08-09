using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <summary>
    /// Renames the fourth Path from Channeler to Hallow (PLAN.md §4.5).
    /// </summary>
    /// <remarks>
    /// Data-only: no column changes shape. It exists because <c>characters.path</c> is stored as
    /// the enum's <em>name</em> rather than its ordinal - a deliberate choice, since a text column
    /// is readable in a psql session and survives someone reordering the enum, but it does mean a
    /// rename is a data migration. Without this, every existing Channeler fails to materialise
    /// and takes its account's character list down with it.
    ///
    /// The ability rows need no statement here. They are reconciled against
    /// <c>AbilityCatalogue</c> on every boot, which deletes the <c>channeler.*</c> keys and
    /// inserts the <c>hallow.*</c> ones - and nothing references an ability key from another
    /// table, because <c>character_abilities</c> (§6) is planned and not yet built. Renaming after
    /// it exists would have been a second, harder statement; this is the cheap moment.
    /// </remarks>
    public partial class RenameChannelerPathToHallow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE characters SET path = 'Hallow' WHERE path = 'Channeler';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE characters SET path = 'Channeler' WHERE path = 'Hallow';");
        }
    }
}
