namespace Muwbta.Domain.Abilities.Effects;

/// <summary>
/// Registry of all available ability effects. Resolves effect keys to effect instances.
/// Registered as a singleton in DI so effects are available on the game loop thread.
/// </summary>
public sealed class EffectRegistry
{
    private readonly Dictionary<string, IAbilityEffect> _effects = [];

    public EffectRegistry()
    {
        // Register all built-in effects
        Register(new DamageEffect());
        Register(new HealEffect());
        Register(new BuffEffect());
        Register(new DebuffEffect());
        Register(new DamageOverTimeEffect());
        Register(new StunEffect());
        Register(new RootEffect());
        Register(new TauntEffect());
        Register(new DefenseEffect());
        Register(new ExposeEffect());
        Register(new MaxHealthEffect());
        Register(new ResourceEffect());
    }

    public void Register(IAbilityEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effects[effect.EffectKey] = effect;
    }

    public IAbilityEffect? Get(string effectKey)
    {
        ArgumentNullException.ThrowIfNull(effectKey);
        _effects.TryGetValue(effectKey, out var effect);
        return effect;
    }

    public bool Contains(string effectKey) =>
        !string.IsNullOrEmpty(effectKey) && _effects.ContainsKey(effectKey);

    /// <summary>
    /// Whether this ability points at somebody - true when *any* of its effects is harmful.
    /// </summary>
    /// <remarks>
    /// One definition, because two places need the answer and they must not disagree: the cast
    /// loop uses it to decide whether landing the ability opens a fight, and the command layer
    /// uses it to decide who a bare `cast` should fall back to. An ability that damages and
    /// debuffs is an attack whichever order it was written in.
    ///
    /// <see cref="AbilityValidator"/> refuses a list that mixes directions, so in practice this is
    /// reading a decision rather than resolving a conflict.
    /// </remarks>
    public bool IsHarmful(Ability ability)
    {
        ArgumentNullException.ThrowIfNull(ability);
        return ability.Effects.Exists(e => Get(e.Key)?.IsHarmful == true);
    }
}
