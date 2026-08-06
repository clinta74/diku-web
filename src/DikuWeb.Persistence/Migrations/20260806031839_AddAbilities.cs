using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAbilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Abilities",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CostType = table.Column<int>(type: "integer", nullable: false),
                    CostValue = table.Column<int>(type: "integer", nullable: false),
                    CooldownPulses = table.Column<long>(type: "bigint", nullable: false),
                    CastTimePulses = table.Column<long>(type: "bigint", nullable: true),
                    TargetingType = table.Column<int>(type: "integer", nullable: false),
                    EffectKey = table.Column<string>(type: "text", nullable: false),
                    EffectParams = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Abilities", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Abilities_TargetingType",
                table: "Abilities",
                column: "TargetingType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Abilities");
        }
    }
}
