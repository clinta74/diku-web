using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRespawnAndGold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CurrentTarget",
                table: "characters",
                newName: "current_target");

            migrationBuilder.RenameColumn(
                name: "CombatState",
                table: "characters",
                newName: "combat_state");

            migrationBuilder.AlterColumn<string>(
                name: "current_target",
                table: "characters",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "gold",
                table: "characters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "respawn_room_key",
                table: "characters",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gold",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "respawn_room_key",
                table: "characters");

            migrationBuilder.RenameColumn(
                name: "current_target",
                table: "characters",
                newName: "CurrentTarget");

            migrationBuilder.RenameColumn(
                name: "combat_state",
                table: "characters",
                newName: "CombatState");

            migrationBuilder.AlterColumn<string>(
                name: "CurrentTarget",
                table: "characters",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
