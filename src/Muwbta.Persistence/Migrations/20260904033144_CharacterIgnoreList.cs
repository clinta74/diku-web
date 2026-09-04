using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CharacterIgnoreList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default is for the rows that already exist: without it Postgres refuses to add
            // a NOT NULL column to a table that has any, which is every deployment but a fresh one
            // - and the test database is a fresh one, which is why the suite did not notice.
            // Not declared on the model, because the code always supplies a value; it is only
            // here so the migration can run.
            migrationBuilder.AddColumn<List<string>>(
                name: "ignored_names",
                table: "characters",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ignored_names",
                table: "characters");
        }
    }
}
