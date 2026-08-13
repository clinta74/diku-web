using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <summary>
    /// <c>spawners.sentinel bool not null</c> becomes <c>spawners.wanders bool null</c>, where null
    /// means "follow the mob template" (PLAN.md §4.8).
    /// </summary>
    /// <remarks>
    /// <b>Hand-written, because the scaffold dropped the column and added the new one.</b> That is
    /// correct for the schema and wrong for the content: every spawner a builder had deliberately
    /// marked sentinel - the shopkeepers and quest givers, which is most of the ones anybody set -
    /// would have come back with no opinion at all. A rename carries the values across, and then
    /// one UPDATE says what they now mean.
    ///
    /// <b>The two old values do not map onto the new ones symmetrically, on purpose.</b>
    /// <list type="bullet">
    /// <item><c>sentinel = true</c> was somebody ticking a box, so it becomes an explicit
    /// <c>wanders = false</c> - the spawner keeps overriding, and the mob stays put whatever its
    /// template later says.</item>
    /// <item><c>sentinel = false</c> was the column default on every row nobody touched, so it
    /// becomes <c>null</c>: defer to the template. Reading it as an explicit "these wander" would
    /// take the new default away from every spawner in the database before a builder had ever seen
    /// it, which is the whole change being made here.</item>
    /// </list>
    /// The visible consequence, stated rather than discovered: mobs that wandered only because
    /// nothing said otherwise now stand still until a template or a spawner asks them to move.
    /// </remarks>
    public partial class SpawnerWanderTriState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE spawners RENAME COLUMN sentinel TO wanders;");
            migrationBuilder.Sql("ALTER TABLE spawners ALTER COLUMN wanders DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE spawners ALTER COLUMN wanders DROP NOT NULL;");

            // The column still holds sentinel's polarity at this point: true meant "does not
            // wander". Flip the ones that were set, and blank the ones that were merely default.
            migrationBuilder.Sql(
                "UPDATE spawners SET wanders = CASE WHEN wanders THEN false ELSE NULL END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lossy, and unavoidably so: one bool cannot hold three answers. "Follow the template"
            // and "always wander" both go back as sentinel = false, which is what they would have
            // been before this migration existed.
            migrationBuilder.Sql(
                "UPDATE spawners SET wanders = CASE WHEN wanders IS FALSE THEN true ELSE false END;");

            migrationBuilder.Sql("ALTER TABLE spawners ALTER COLUMN wanders SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE spawners ALTER COLUMN wanders SET DEFAULT false;");
            migrationBuilder.Sql("ALTER TABLE spawners RENAME COLUMN wanders TO sentinel;");
        }
    }
}
