using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <summary>
    /// <c>abilities.effect_key</c> and <c>effect_params</c> become one ordered
    /// <c>effects jsonb</c> list, so an ability can do more than one thing (PLAN.md §4.5).
    /// </summary>
    /// <remarks>
    /// <b>Hand-written, because the scaffold drops both columns and adds an empty one.</b> That
    /// would leave every ability on every existing server with no effects at all - which is not a
    /// broken ability so much as an absent one: it charges its cost, starts its cooldown, and does
    /// nothing. The backfill wraps what is already there in a one-element list, which is exactly
    /// what all thirty-seven of them are.
    ///
    /// <b>The JSON keys are PascalCase on purpose.</b> Npgsql maps this column to
    /// <c>List&lt;AbilityEffectSpec&gt;</c> through System.Text.Json with default options, so it
    /// reads <c>Key</c> and <c>Params</c> rather than <c>key</c> and <c>params</c> - the same
    /// casing the neighbouring <c>mob_templates.attacks</c> column already stores. Getting this
    /// wrong would deserialise to a list of empty specs rather than failing, which is the quiet
    /// version of the same outage.
    ///
    /// <c>jsonb_build_array</c> and <c>jsonb_build_object</c> rather than string concatenation:
    /// an ability description containing a quote would otherwise produce invalid JSON, and there
    /// are thirty-seven descriptions written by a person.
    /// </remarks>
    public partial class AbilityEffectList : Migration
    {
        /// <summary>The backfill, exposed so a test can run the real statement (see the last two migrations).</summary>
        public const string BackfillSql = """
            UPDATE abilities
            SET effects = jsonb_build_array(
                jsonb_build_object(
                    'Key', effect_key,
                    'Params', COALESCE(effect_params, '{}'::jsonb)
                )
            )
            WHERE effect_key IS NOT NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "effects",
                table: "abilities",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql(BackfillSql);

            // Only once every row has been carried across. Dropping first would make the backfill
            // impossible to write and the migration impossible to check.
            migrationBuilder.DropColumn(name: "effect_key", table: "abilities");
            migrationBuilder.DropColumn(name: "effect_params", table: "abilities");

            // The default existed to make the column addable to populated rows; leaving it would
            // let an insert that forgot its effects look like a deliberate empty list.
            migrationBuilder.Sql("ALTER TABLE abilities ALTER COLUMN effects DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "effect_key",
                table: "abilities",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "effect_params",
                table: "abilities",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            // Lossy, and unavoidably so: two columns cannot hold a list. Anything past the first
            // effect is dropped, which is the shape the schema had before this migration.
            migrationBuilder.Sql("""
                UPDATE abilities
                SET effect_key = COALESCE(effects -> 0 ->> 'Key', ''),
                    effect_params = COALESCE(effects -> 0 -> 'Params', '{}'::jsonb)
                WHERE jsonb_array_length(effects) > 0;
                """);

            migrationBuilder.DropColumn(name: "effects", table: "abilities");
        }
    }
}
