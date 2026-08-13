using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <summary>
    /// Retunes twenty-five ability cooldowns, and lands Shield Bash's stun on the combat beat.
    /// </summary>
    /// <remarks>
    /// <b>A migration rather than a catalogue edit, because a catalogue edit reaches nobody.</b>
    /// <c>AbilityCatalogue</c> seeds a database that has no rows; every existing server already
    /// holds all thirty-seven, and the reconcile deliberately no longer overwrites them. So the
    /// numbers were changed in both places: the catalogue for a fresh install, and here for the
    /// servers that exist.
    ///
    /// <b>Two design rules produced these values.</b>
    ///
    /// First, every cooldown is a whole number of two-second beats, because a swing is 8 pulses
    /// (PLAN.md §2.3) and a cooldown that is not a multiple of it drifts against the rhythm of the
    /// fight forever. Fourteen of the thirty-seven were fractional - Quick Strike at 1.25 beats,
    /// Shield Bash at 21.5.
    ///
    /// Second, length follows how much an ability changes the fight. For anything with a duration
    /// that meant cooldown = duration / target uptime, and that is where the large moves are: ten
    /// of the eleven timed effects had a duration *longer than their own cooldown* and all of them
    /// refresh, so they were permanently maintainable and the cooldown was decorative. Weaken sat
    /// at 200% uptime, Scorch at 225%. A permanently weakened enemy is not an ability, it is a
    /// difficulty setting.
    ///
    /// Ambush moves the other way, 28 pulses down to 16, because it is authored to stack three
    /// times and could never reach two: the first stack expired before a third could land.
    ///
    /// <b>Guarded on the old value, so a hand-tuned row is left alone.</b> The same rule the
    /// spawner wander migration followed. An ability whose cooldown is neither the old number nor
    /// the new one is somebody's deliberate edit, and a migration that overwrote it would undo
    /// their work on deploy - silently, which is the worst way for it to happen. The cost is that
    /// a server which had already retuned to the old value by hand keeps it; that is the right
    /// trade, since the alternative destroys information and this only declines to add any.
    /// </remarks>
    public partial class AbilityCooldownRetune : Migration
    {
        /// <summary>Old and new, so <c>Down</c> is exact rather than approximate.</summary>
        public const string RetuneSql = """
            UPDATE abilities AS a
            SET cooldown_pulses = v.new_cd
            FROM (VALUES
                ('warden.kick', 20, 24),
                ('warden.bash', 24, 32),
                ('warden.sunder', 144, 160),
                ('warden.shield-bash', 172, 160),
                ('warden.rally', 120, 96),
                ('warden.shield-wall', 90, 240),
                ('warden.crushing-blow', 36, 48),
                ('adept.bolt', 12, 24),
                ('adept.weaken', 40, 160),
                ('adept.amplify', 64, 200),
                ('adept.scorch', 32, 72),
                ('adept.enfeeble', 56, 240),
                ('adept.disjunction', 40, 56),
                ('adept.cataclysm', 160, 192),
                ('shade.strike', 10, 24),
                ('shade.fortify', 56, 176),
                ('shade.hamstring', 60, 128),
                ('shade.ambush', 28, 16),
                ('shade.assassinate', 44, 64),
                ('shade.death-mark', 150, 192),
                ('hallow.mend', 20, 24),
                ('hallow.wither', 44, 96),
                ('hallow.blessing', 72, 240),
                ('hallow.sap', 60, 240),
                ('hallow.intercession', 180, 176)
            ) AS v(key, old_cd, new_cd)
            WHERE a.key = v.key AND a.cooldown_pulses = v.old_cd;
            """;

        /// <summary>The same list, reversed. Only rows still holding the new value go back.</summary>
        public const string RevertSql = """
            UPDATE abilities AS a
            SET cooldown_pulses = v.old_cd
            FROM (VALUES
                ('warden.kick', 20, 24),
                ('warden.bash', 24, 32),
                ('warden.sunder', 144, 160),
                ('warden.shield-bash', 172, 160),
                ('warden.rally', 120, 96),
                ('warden.shield-wall', 90, 240),
                ('warden.crushing-blow', 36, 48),
                ('adept.bolt', 12, 24),
                ('adept.weaken', 40, 160),
                ('adept.amplify', 64, 200),
                ('adept.scorch', 32, 72),
                ('adept.enfeeble', 56, 240),
                ('adept.disjunction', 40, 56),
                ('adept.cataclysm', 160, 192),
                ('shade.strike', 10, 24),
                ('shade.fortify', 56, 176),
                ('shade.hamstring', 60, 128),
                ('shade.ambush', 28, 16),
                ('shade.assassinate', 44, 64),
                ('shade.death-mark', 150, 192),
                ('hallow.mend', 20, 24),
                ('hallow.wither', 44, 96),
                ('hallow.blessing', 72, 240),
                ('hallow.sap', 60, 240),
                ('hallow.intercession', 180, 176)
            ) AS v(key, old_cd, new_cd)
            WHERE a.key = v.key AND a.cooldown_pulses = v.new_cd;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RetuneSql);

            // 10 pulses is 2.5s against a 2s swing: it reliably eats one and sometimes two, which
            // is why a stun never felt like a fixed thing. 16 is exactly two swings.
            migrationBuilder.Sql("""
                UPDATE abilities
                SET effect_params = jsonb_set(effect_params, '{durationPulses}', '"16"')
                WHERE key = 'warden.shield-bash'
                  AND effect_key = 'control.stun'
                  AND effect_params ->> 'durationPulses' = '10';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RevertSql);

            migrationBuilder.Sql("""
                UPDATE abilities
                SET effect_params = jsonb_set(effect_params, '{durationPulses}', '"10"')
                WHERE key = 'warden.shield-bash'
                  AND effect_key = 'control.stun'
                  AND effect_params ->> 'durationPulses' = '16';
                """);
        }
    }
}
