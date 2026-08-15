using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConditionalExits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refusal_message",
                table: "room_exits",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "required_flag_key",
                table: "room_exits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "required_item_key",
                table: "room_exits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reward_flag_key",
                table: "quests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // defaultValueSql, not a bare NOT NULL. EF generates the latter for a non-nullable
            // column it has no default for, and that statement fails outright on any database
            // that already has a character in it - which is every database this will ever run
            // against. An existing character holds no capabilities, so '{}' is both the correct
            // backfill and the right default for anything inserting outside EF.
            migrationBuilder.AddColumn<List<string>>(
                name: "flags",
                table: "characters",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refusal_message",
                table: "room_exits");

            migrationBuilder.DropColumn(
                name: "required_flag_key",
                table: "room_exits");

            migrationBuilder.DropColumn(
                name: "required_item_key",
                table: "room_exits");

            migrationBuilder.DropColumn(
                name: "reward_flag_key",
                table: "quests");

            migrationBuilder.DropColumn(
                name: "flags",
                table: "characters");
        }
    }
}
