using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemRestrictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_lore",
                table: "item_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_no_drop",
                table: "item_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // defaultValueSql, not defaultValue. EF generated `defaultValue: ""` because the
            // value converter hands it a string, and an empty string is not valid jsonb - the
            // ALTER TABLE fails outright on any database that already has item templates, which
            // is every database. The empty array is the value an unrestricted item wants anyway.
            migrationBuilder.AddColumn<string>(
                name: "paths",
                table: "item_templates",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_lore",
                table: "item_templates");

            migrationBuilder.DropColumn(
                name: "is_no_drop",
                table: "item_templates");

            migrationBuilder.DropColumn(
                name: "paths",
                table: "item_templates");
        }
    }
}
