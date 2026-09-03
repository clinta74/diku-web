using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuestPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "paths",
                table: "quests",
                type: "jsonb",
                nullable: false,

                // '[]'::jsonb, not "". EF's scaffolded default for a string-converted column is an
                // empty string, and Postgres refuses that as jsonb - the migration would fail on
                // any database with quests already in it. The item_templates.paths column, which is
                // the same conversion, is written the same way for the same reason.
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paths",
                table: "quests");
        }
    }
}
