using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurationWorlds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default is for the rows that already exist, as with characters.ignored_names:
            // Postgres refuses a NOT NULL column on a table with rows otherwise, and every
            // deployment but a fresh one has rows. Not declared on the model; the code always
            // supplies a value.
            migrationBuilder.AddColumn<List<string>>(
                name: "world_keys",
                table: "game_configurations",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "world_keys",
                table: "game_configurations");
        }
    }
}
