using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muwbta.Persistence.Migrations
{
    /// <summary>
    /// One slot becomes a list of them, plus the flag for a weapon that claims both hands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order matters and the scaffolded version had it wrong.</b> EF wrote
    /// <c>DropColumn(slot)</c> first and then added <c>slots</c> with no conversion between them,
    /// which on a populated database is every authored item arriving equippable nowhere — armour
    /// that cannot be worn and weapons that cannot be held, silently, with the old value already
    /// gone. Add, convert, then drop.
    /// </para>
    /// <para>
    /// It also scaffolded <c>defaultValue: ""</c> for a jsonb column, which Postgres refuses
    /// outright — the same defect as the <c>QuestPaths</c> migration, from the same cause: EF's
    /// default for a string-converted column is an empty string, and jsonb is not a string.
    /// </para>
    /// </remarks>
    public partial class ItemSlotList : Migration
    {
        /// <summary>
        /// The <c>ItemSlot</c> members by their stored ordinal.
        /// </summary>
        /// <remarks>
        /// Spelled out rather than derived, because a migration has to mean the same thing forever:
        /// it converts the numbers that are in the database <em>now</em>, and it must not start
        /// meaning something else the day a slot is inserted into the middle of the enum. Storing
        /// names from here on is what stops the next one having this problem.
        /// </remarks>
        private static readonly string[] SlotNames =
            ["Head", "Chest", "Hands", "Legs", "Feet", "MainHand", "OffHand", "Trinket"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "slots",
                table: "item_templates",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "is_two_handed",
                table: "item_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // One slot becomes a list of one; a null slot becomes the empty list, which is the same
            // "not equippable" it always meant.
            for (var ordinal = 0; ordinal < SlotNames.Length; ordinal++)
            {
                migrationBuilder.Sql(
                    $"""
                    UPDATE item_templates
                       SET slots = '["{SlotNames[ordinal]}"]'::jsonb
                     WHERE slot = {ordinal};
                    """);
            }

            migrationBuilder.DropColumn(
                name: "slot",
                table: "item_templates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "slot",
                table: "item_templates",
                type: "integer",
                nullable: true);

            // The first slot of the list, which is all a single column can hold. Going back is
            // lossy on purpose and by necessity: an either-hand weapon has no v14 spelling, so it
            // returns as a main-hand one, and two-handedness has nowhere to go at all.
            for (var ordinal = 0; ordinal < SlotNames.Length; ordinal++)
            {
                migrationBuilder.Sql(
                    $"""
                    UPDATE item_templates
                       SET slot = {ordinal}
                     WHERE slots->>0 = '{SlotNames[ordinal]}';
                    """);
            }

            migrationBuilder.DropColumn(
                name: "is_two_handed",
                table: "item_templates");

            migrationBuilder.DropColumn(
                name: "slots",
                table: "item_templates");
        }
    }
}
