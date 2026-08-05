using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worlds",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worlds", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "zones",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    world_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    min_level = table.Column<int>(type: "integer", nullable: false),
                    max_level = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zones", x => x.key);
                    table.ForeignKey(
                        name: "FK_zones_worlds_world_key",
                        column: x => x.world_key,
                        principalTable: "worlds",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    zone_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    grid = table.Column<string[]>(type: "text[]", nullable: false),
                    legend = table.Column<IReadOnlyDictionary<string, string>>(type: "jsonb", nullable: false),
                    editor_x = table.Column<int>(type: "integer", nullable: true),
                    editor_y = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.key);
                    table.ForeignKey(
                        name: "FK_rooms_zones_zone_key",
                        column: x => x.zone_key,
                        principalTable: "zones",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "room_exits",
                columns: table => new
                {
                    from_room_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    to_room_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_exits", x => new { x.from_room_key, x.direction });
                    table.ForeignKey(
                        name: "FK_room_exits_rooms_from_room_key",
                        column: x => x.from_room_key,
                        principalTable: "rooms",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_room_exits_to_room_key",
                table: "room_exits",
                column: "to_room_key");

            migrationBuilder.CreateIndex(
                name: "ix_rooms_zone_key",
                table: "rooms",
                column: "zone_key");

            migrationBuilder.CreateIndex(
                name: "ix_zones_world_key",
                table: "zones",
                column: "world_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_exits");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropTable(
                name: "zones");

            migrationBuilder.DropTable(
                name: "worlds");
        }
    }
}
