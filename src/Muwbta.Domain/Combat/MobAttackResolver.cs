using Muwbta.Domain.Inhabitants;

namespace Muwbta.Domain.Combat;

/// <summary>
/// Turns a mob template's authored attack list into the attacks combat will actually run.
/// </summary>
/// <remarks>
/// This is the single place an empty list becomes the default attack, and it is deliberately a
/// query rather than something stamped onto the mob at spawn. Resolving on read means a builder
/// retiming a mob changes a fight already in progress, and an empty array keeps meaning "empty"
/// in the database - so the default can be changed later without rewriting every row.
/// </remarks>
public static class MobAttackResolver
{
    private static readonly IReadOnlyList<MobAttack> Default =
    [
        new MobAttack
        {
            Verb = AttackTiming.DefaultVerb,
            DelayPulses = AttackTiming.DefaultDelayPulses,
        },
    ];

    /// <summary>
    /// The attacks a mob of this template swings with. Never empty: a template that declares
    /// nothing gets one default attack, which is what every mob did before attacks were authorable.
    /// </summary>
    public static IReadOnlyList<MobAttack> Resolve(MobTemplate? template)
    {
        if (template?.Attacks is not { Count: > 0 } authored)
        {
            return Default;
        }

        var resolved = new List<MobAttack>(authored.Count);

        foreach (var attack in authored)
        {
            if (attack is null)
            {
                continue;
            }

            resolved.Add(new MobAttack
            {
                Verb = AttackTiming.VerbOr(attack.Verb),
                DelayPulses = AttackTiming.Clamp(attack.DelayPulses),
                DamageMultiplier = attack.DamageMultiplier is > 0m ? attack.DamageMultiplier : null,

                // Blank is the same as absent. An editor that writes "" for an untouched dropdown
                // would otherwise produce an attack that looks up an effect named nothing on
                // every single swing.
                EffectKey = string.IsNullOrWhiteSpace(attack.EffectKey) ? null : attack.EffectKey.Trim(),
                EffectParams = attack.EffectParams is { Count: > 0 }
                    ? new Dictionary<string, string>(attack.EffectParams, StringComparer.Ordinal)
                    : null,
            });
        }

        return resolved.Count == 0 ? Default : resolved;
    }
}
