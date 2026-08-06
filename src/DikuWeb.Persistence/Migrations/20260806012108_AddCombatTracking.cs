using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCombatTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CombatState",
                table: "Mobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurrentTarget",
                table: "Mobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CombatState",
                table: "characters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurrentTarget",
                table: "characters",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CombatState",
                table: "Mobs");

            migrationBuilder.DropColumn(
                name: "CurrentTarget",
                table: "Mobs");

            migrationBuilder.DropColumn(
                name: "CombatState",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "CurrentTarget",
                table: "characters");
        }
    }
}
