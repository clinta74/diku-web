using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_login_address",
                table: "accounts",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "registered_from_address",
                table: "accounts",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_login_address",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "registered_from_address",
                table: "accounts");
        }
    }
}
