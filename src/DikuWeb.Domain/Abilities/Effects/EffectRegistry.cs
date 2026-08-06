namespace DikuWeb.Domain.Abilities.Effects;

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
}
