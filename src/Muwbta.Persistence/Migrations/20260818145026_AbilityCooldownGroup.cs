using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AbilityCooldownGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cooldown_group",
                table: "abilities",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cooldown_group",
                table: "abilities");
        }
    }
}
