using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <summary>
    /// A spawner may pin the level its mobs fight at, instead of letting the zone decide
    /// (PLAN.md §4.7).
    /// </summary>
    /// <remarks>
    /// <b>Left as EF scaffolded it, which is unusual here and is the point.</b> Three migrations in
    /// this folder had to be hand-rewritten because the scaffold's idea of a change was to drop and
    /// recreate; this one adds a nullable column and drops it again, so it is additive in one
    /// direction and lossless in the other. <c>SpawnerWanderTriState</c>'s <c>Down</c> hedges
    /// because it genuinely loses information — this one does not, and saying so is worth more than
    /// copying the hedge.
    ///
    /// No backfill. NULL already means "let the zone decide", which is exactly what every existing
    /// spawner meant before the column existed.
    /// </remarks>
    public partial class SpawnerFightsAtLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fights_at_level",
                table: "spawners",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fights_at_level",
                table: "spawners");
        }
    }
}
