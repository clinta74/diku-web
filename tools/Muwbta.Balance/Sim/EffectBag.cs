using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;

namespace Muwbta.Balance.Sim;

/// <summary>
/// The effects riding on one combatant, with the collision rules <c>WorldState.ApplyEffect</c>
/// uses.
/// </summary>
/// <remarks>
/// <para>
/// <b>A copy of those rules, and the only copy in this tool.</b> <c>WorldState</c> is an Engine
/// type wired to a live world — rooms, players, a change feed — and standing one up to measure a
/// damage number would drag the whole server into the harness. What is reproduced here is the part
/// that decides damage: dedupe on (key, source), a stronger application replacing a weaker one, and
/// <see cref="EffectStackingRule"/> deciding what a recast at equal strength does.
/// </para>
/// <para>
/// The maximum-health grant is reproduced too, because it is not bookkeeping — Last Stand and its
/// siblings are most of what keeps a Warden alive, and a harness that raised the ceiling without
/// handing over the health would report the Path dying to fights it survives.
/// </para>
/// </remarks>
public sealed class EffectBag
{
    private readonly List<ActiveEffect> _effects = [];

    public IReadOnlyList<ActiveEffect> All => _effects;

    /// <summary>Whether anything here stops its bearer from acting: a stun, a root that binds.</summary>
    public bool Incapacitated => _effects.Any(e => e.PreventsActing);

    /// <summary>The mitigation these effects add to whatever the gear already absorbs.</summary>
    public decimal MitigationDelta => _effects.Sum(e => e.MitigationDelta);

    /// <summary>The defence rating these effects add or take away.</summary>
    public int DefenseRatingDelta => _effects.Sum(e => e.DefenseRatingDelta);

    /// <summary>Whether an effect of this key from this source is already running.</summary>
    public bool Has(string effectKey, string sourceId) =>
        Find(effectKey, sourceId) is not null;

    /// <summary>
    /// How many pulses are left on an effect of this key from this source, or zero if none is
    /// running. Used by the rotation to avoid clipping a wound it has already opened.
    /// </summary>
    public long Remaining(string effectKey, string sourceId, long currentPulse) =>
        Find(effectKey, sourceId) is { } effect
            ? Math.Max(0, effect.ExpiresAtPulse - currentPulse)
            : 0;

    /// <summary>Mirrors <c>WorldState.ApplyEffect</c>, including the max-health grant.</summary>
    public void Apply(ActiveEffect effect, Vitals? vitals)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var existing = Find(effect.EffectKey, effect.SourceEntityId);

        if (existing is null)
        {
            _effects.Add(effect);
            GrantMaxHealth(effect, vitals);
            return;
        }

        if (effect.SourceUnlockLevel > existing.SourceUnlockLevel)
        {
            _effects.Remove(existing);
            RevokeMaxHealth(existing, vitals);

            _effects.Add(effect);
            GrantMaxHealth(effect, vitals);
            return;
        }

        if (effect.SourceUnlockLevel < existing.SourceUnlockLevel)
        {
            // A weaker application does nothing at all - it does not stack and it does not extend.
            return;
        }

        switch (existing.StackingRule)
        {
            case EffectStackingRule.Refresh:
                existing.ExpiresAtPulse = effect.ExpiresAtPulse;
                break;

            case EffectStackingRule.Stack:
                if (existing.Stacks < existing.MaxStacks)
                {
                    existing.Stacks++;
                }

                existing.ExpiresAtPulse = effect.ExpiresAtPulse;
                break;

            case EffectStackingRule.Ignore:
                break;
        }
    }

    /// <summary>
    /// Drops everything whose expiry has arrived.
    /// </summary>
    /// <remarks>
    /// Every pulse here, where the server sweeps on the 60-second tick and re-checks expiry at
    /// each DoT tick. The server's arrangement exists so a wound cannot outlive itself by up to a
    /// minute; sweeping every pulse reaches the same place with less bookkeeping, and the one thing
    /// it changes - when a buff's *multiplier* stops counting - is a difference of at most a minute
    /// in the server's favour, so this is the conservative direction.
    /// </remarks>
    public void Expire(long currentPulse, Vitals? vitals)
    {
        for (var i = _effects.Count - 1; i >= 0; i--)
        {
            if (_effects[i].ExpiresAtPulse > currentPulse)
            {
                continue;
            }

            RevokeMaxHealth(_effects[i], vitals);
            _effects.RemoveAt(i);
        }
    }

    private ActiveEffect? Find(string effectKey, string sourceId) =>
        _effects.FirstOrDefault(e =>
            string.Equals(e.EffectKey, effectKey, StringComparison.Ordinal) &&
            string.Equals(e.SourceEntityId, sourceId, StringComparison.Ordinal));

    private static void GrantMaxHealth(ActiveEffect effect, Vitals? vitals)
    {
        if (effect.MaxHealthDelta <= 0 || vitals is null)
        {
            return;
        }

        vitals.HealthMax += effect.MaxHealthDelta;
        vitals.Health += effect.MaxHealthDelta;
    }

    private static void RevokeMaxHealth(ActiveEffect effect, Vitals? vitals)
    {
        if (effect.MaxHealthDelta <= 0 || vitals is null)
        {
            return;
        }

        vitals.HealthMax = Math.Max(1, vitals.HealthMax - effect.MaxHealthDelta);
        vitals.Health = Math.Min(vitals.Health, vitals.HealthMax);
    }
}
