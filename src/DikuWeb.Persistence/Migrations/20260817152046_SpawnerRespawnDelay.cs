using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <summary>
    /// Adds <c>spawners.respawn_seconds</c> back, this time with something reading it.
    /// </summary>
    /// <remarks>
    /// The column existed from the start, was read by nothing, and was dropped by
    /// <c>DropUnreadDials</c> for exactly that reason. It returns because the design wants it: some
    /// things should be rarer than others, and a boss that comes back on the sweep's own cadence is
    /// not a boss (PLAN.md §4.8, BUGS.md #17).
    ///
    /// <b>The default is 60, not the 0 EF scaffolds.</b> Zero means "replace on the next sweep",
    /// which is precisely the behaviour this feature exists to end — so a database whose rows
    /// defaulted to zero would upgrade into the old bug rather than out of it.
    /// </remarks>
    public partial class SpawnerRespawnDelay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "respawn_seconds",
                table: "spawners",
                type: "integer",
                nullable: false,
                defaultValue: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "respawn_seconds",
                table: "spawners");
        }
    }
}
