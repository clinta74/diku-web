using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// What a mob attack's rider is called when the attack does not name it.
/// </summary>
/// <remarks>
/// Reported from play as <em>"You are control.stun!"</em>. The narration fell back to the effect
/// <em>key</em> when a rider authored no <c>name</c> — and that fallback was never needed, because
/// every effect that carries a status already resolves one of its own. It now narrates from the
/// <see cref="ActiveEffect"/> that was actually applied, so the line and the status panel cannot
/// disagree about what to call it.
///
/// The frame is what makes these worth pinning individually: each name is dropped into
/// <em>"You are …!"</em>, so a noun is wrong there however well it reads as a panel label.
/// <c>debuff.weaken</c> defaulted to "weakness" and said <em>"You are weakness!"</em>.
/// </remarks>
public sealed class RiderNarrationTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>Hits the player once with an attack carrying <paramref name="effectKey"/>.</summary>
    private static string Struck(string effectKey, Dictionary<string, string>? parameters = null)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Kael", West, level: 10);
        var mob = harness.AddMob(
            "rat",
            West,
            attacks: [new MobAttack
            {
                DelayPulses = 8,
                Verb = "bite",
                EffectKey = effectKey,
                EffectParams = parameters,
            }],
            health: 1000);

        mob.ResolvedStats["attackRating"] = 100;

        harness.Execute(player, "attack rat");
        harness.Drain(player);
        harness.Pump(24);

        return harness.DrainText(player);
    }

    [Theory]
    [InlineData("control.stun", "You are stunned!")]
    [InlineData("control.root", "You are held fast!")]
    [InlineData("damage.overtime", "You are bleeding!")]
    [InlineData("debuff.weaken", "You are weakened!")]
    [InlineData("debuff.expose", "You are exposed!")]
    public void An_unnamed_rider_falls_back_to_the_effects_own_word(string key, string expected)
    {
        // Every effect a mob attack may carry, and every one of them in the sentence it lands in.
        var text = Struck(key);

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain(key, text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_authored_name_still_wins()
    {
        var text = Struck("control.stun", new Dictionary<string, string> { ["name"] = "reeling" });

        Assert.Contains("You are reeling!", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stunned", text, StringComparison.Ordinal);
    }

    [Fact]
    public void No_effect_key_ever_reaches_the_player()
    {
        // The defect class rather than the instance. A key is an authoring identifier; the only
        // reason one was ever printed is that a fallback reached for the nearest string to hand.
        foreach (var key in new[]
                 {
                     "control.stun", "control.root", "damage.overtime",
                     "debuff.weaken", "debuff.expose",
                 })
        {
            Assert.DoesNotContain(".", Word(Struck(key)), StringComparison.Ordinal);
        }

        // Just the sentence the rider announces itself in, not the rest of the fight. Reading to
        // the end of the log meant this passed or failed on whether another line happened to
        // follow - any "You cut a rat for 2 damage." after it carries a dot of its own.
        static string Word(string text)
        {
            var start = text.IndexOf("You are ", StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            var end = text.IndexOf('!', start);
            return end < 0 ? text[start..] : text[start..end];
        }
    }
}
