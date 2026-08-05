using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuilderSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The defaults below were hand-corrected from "" to "{}". EF scaffolds the CLR
            // default of the converted property, which serialises to an empty string, and
            // Postgres rejects '' as jsonb outright - so the generated migration would fail on
            // any database that already had a world in it.
            migrationBuilder.AddColumn<string>(
                name: "flags",
                table: "zones",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "flags",
                table: "worlds",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "flags",
                table: "rooms",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "content_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    entity_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    action = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_audit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_audit_entity",
                table: "content_audit",
                columns: new[] { "entity_kind", "entity_key", "at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_audit");

            migrationBuilder.DropColumn(
                name: "flags",
                table: "zones");

            migrationBuilder.DropColumn(
                name: "flags",
                table: "worlds");

            migrationBuilder.DropColumn(
                name: "flags",
                table: "rooms");
        }
    }
}
