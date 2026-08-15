using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GameConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_configurations",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    starting_room_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    welcome_message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_configurations", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_game_configurations_active",
                table: "game_configurations",
                column: "is_active",
                unique: true,
                filter: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_configurations");
        }
    }
}
