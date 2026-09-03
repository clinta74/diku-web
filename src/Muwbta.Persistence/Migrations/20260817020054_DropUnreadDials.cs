using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <summary>
    /// Drops <c>spawners.respawn_seconds</c>, which nothing has ever read.
    /// </summary>
    /// <remarks>
    /// EF warns that this may lose data, and it does: every one of the 100 authored spawners
    /// carries a number here. None of them ever meant anything - SpawnerSystem refills to
    /// TargetCount on its own 15-second sweep and never consulted the column, so a builder who
    /// typed "respawn after 600 seconds" got 15 (BUGS.md #17).
    ///
    /// The two multipliers deleted alongside it, <c>itemPower</c> and <c>spawnDensity</c>, need no
    /// migration: they live inside the <c>multipliers</c> jsonb, and the converter simply stops
    /// writing them. Keys already in a stored document are ignored on read, so old rows load
    /// cleanly and shed the dead keys the next time they are saved.
    /// </remarks>
    public partial class DropUnreadDials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "respawn_seconds",
                table: "spawners");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "respawn_seconds",
                table: "spawners",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
