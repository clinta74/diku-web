using System.Collections.Generic;
using DikuWeb.Domain.Inhabitants;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttackTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValueSql is hand-added: Postgres refuses a NOT NULL column on a populated
            // table without one, and every existing mob template predates this column. An empty
            // array is also the value that means "one default attack" at read time, so existing
            // mobs keep swinging exactly as they did.
            migrationBuilder.AddColumn<List<MobAttack>>(
                name: "attacks",
                table: "mob_templates",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "attack_delay_pulses",
                table: "item_templates",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attack_verb",
                table: "item_templates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attacks",
                table: "mob_templates");

            migrationBuilder.DropColumn(
                name: "attack_delay_pulses",
                table: "item_templates");

            migrationBuilder.DropColumn(
                name: "attack_verb",
                table: "item_templates");
        }
    }
}
