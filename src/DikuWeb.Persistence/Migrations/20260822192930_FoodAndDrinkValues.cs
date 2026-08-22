using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FoodAndDrinkValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "drink_value",
                table: "item_templates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "food_value",
                table: "item_templates",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "drink_value",
                table: "item_templates");

            migrationBuilder.DropColumn(
                name: "food_value",
                table: "item_templates");
        }
    }
}
