using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <summary>
    /// <c>abilities</c> gains <c>path</c> and <c>unlock_level</c>, so the table holds who learns an
    /// ability and when rather than only what it does (PLAN.md §4.5).
    /// </summary>
    /// <remarks>
    /// <b>The backfill is the point of this migration, and the scaffold did not have one.</b>
    /// Adding two non-null columns with a default of 0 makes every existing row a Warden ability
    /// unlocked at level 0 - which is to say every character of every Path would know all
    /// thirty-seven abilities from level 1. Every database that has ever booted since 5.1e already
    /// holds all thirty-seven rows, because <c>ReconcileAbilitiesAsync</c> has been keeping the
    /// table matched to the catalogue on every startup, so this is not a theoretical case: it is
    /// every deployment.
    ///
    /// <b>The pairs below are literals and must stay literals.</b> They are not read from
    /// <c>AbilityCatalogue</c>, even though that is where they came from, because a migration that
    /// reads live code produces a different result depending on when it is run - and the retune
    /// that follows this change edits exactly that code. A migration is a statement about what the
    /// schema looked like at one moment; it has to keep saying the same thing forever.
    ///
    /// <c>path</c> is the <c>CharacterPath</c> ordinal: 0 Warden, 1 Adept, 2 Shade, 3 Hallow.
    ///
    /// A row the backfill does not name keeps path 0 and unlock level 0. Up to this version the
    /// reconcile purged anything the catalogue did not define, so on a stock build there is nothing
    /// for it to miss; from this version on the reconcile stops purging, which is why the guard
    /// matters going forward. <c>AbilityValidator</c> refuses an unlock level below 1, so such a
    /// row surfaces as a complaint rather than as an ability everybody silently knows.
    /// </remarks>
    public partial class AbilityPathAndUnlockLevel : Migration
    {
        /// <summary>
        /// The backfill, exposed so a test can run it rather than a copy of it.
        /// </summary>
        /// <remarks>
        /// A fresh database never exercises this: the columns are added to an empty table and the
        /// seeder then inserts rows that already carry a path. So the statement that matters to
        /// every existing deployment is the one no ordinary test run would touch, which is how a
        /// backfill ships broken. <c>AbilityBackfillTests</c> zeroes the columns and runs this.
        /// </remarks>
        public const string BackfillSql = """
            UPDATE abilities AS a
            SET path = v.path, unlock_level = v.unlock_level
            FROM (VALUES
                ('warden.kick', 0, 1),
                ('warden.bash', 0, 3),
                ('warden.battle-fury', 0, 5),
                ('warden.sunder', 0, 7),
                ('warden.taunt', 0, 8),
                ('warden.shield-bash', 0, 9),
                ('warden.rally', 0, 10),
                ('warden.shield-wall', 0, 13),
                ('warden.crushing-blow', 0, 16),
                ('warden.last-stand', 0, 20),
                ('adept.bolt', 1, 1),
                ('adept.shield', 1, 3),
                ('adept.weaken', 1, 5),
                ('adept.amplify', 1, 7),
                ('adept.scorch', 1, 10),
                ('adept.enfeeble', 1, 13),
                ('adept.disjunction', 1, 16),
                ('adept.firestorm', 1, 18),
                ('adept.cataclysm', 1, 20),
                ('shade.strike', 2, 1),
                ('shade.evasion', 2, 3),
                ('shade.fortify', 2, 5),
                ('shade.hamstring', 2, 7),
                ('shade.ambush', 2, 10),
                ('shade.provoke', 2, 12),
                ('shade.vanish', 2, 13),
                ('shade.assassinate', 2, 16),
                ('shade.death-mark', 2, 20),
                ('hallow.mend', 3, 1),
                ('hallow.guidance', 3, 3),
                ('hallow.wither', 3, 5),
                ('hallow.restore', 3, 7),
                ('hallow.blessing', 3, 10),
                ('hallow.renewal', 3, 13),
                ('hallow.sap', 3, 16),
                ('hallow.benediction', 3, 18),
                ('hallow.intercession', 3, 20)
            ) AS v(key, path, unlock_level)
            WHERE a.key = v.key;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "path",
                table: "abilities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "unlock_level",
                table: "abilities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(BackfillSql);

            // Dropped once the existing rows are answered for. Leaving it would mean an insert that
            // forgot a path silently produced a Warden ability, which is the failure this migration
            // exists to prevent, arriving later by a different door.
            migrationBuilder.Sql("ALTER TABLE abilities ALTER COLUMN path DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE abilities ALTER COLUMN unlock_level DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_abilities_path_unlock_level",
                table: "abilities",
                columns: new[] { "path", "unlock_level" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_abilities_path_unlock_level",
                table: "abilities");

            migrationBuilder.DropColumn(
                name: "path",
                table: "abilities");

            migrationBuilder.DropColumn(
                name: "unlock_level",
                table: "abilities");
        }
    }
}
