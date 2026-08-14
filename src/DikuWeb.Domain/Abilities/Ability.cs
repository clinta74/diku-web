using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Abilities;

/// <summary>
/// An ability (spell, skill, technique) that a character can cast or use.
/// </summary>
/// <remarks>
/// <b>The database is the source of truth, not <see cref="AbilityCatalogue"/>.</b> The catalogue
/// is the starter set a fresh database is seeded from — the same standing as the Millbrook rooms
/// (PLAN.md §6) — and stops being consulted the moment a row exists. Everything a builder can
/// change about an ability lives on this row.
///
/// <see cref="Path"/> and <see cref="UnlockLevel"/> are here rather than only in the catalogue
/// because *who learns this and when* is as much a tuning decision as its cooldown, and leaving
/// them in code would have meant a table that could be edited into a shape the level curve had
/// never heard of. Passives are the deliberate exception and stay in
/// <see cref="AbilityProgression"/>: a passive has no row here, no cost, and nothing to target.
/// </remarks>
public sealed class Ability
{
    /// <summary>Unique key: e.g., "warden.slash" or "adept.bolt".</summary>
    public required string Key { get; init; }

    /// <summary>Which Path learns this ability.</summary>
    public required CharacterPath Path { get; init; }

    /// <summary>The level at which this Path is granted it.</summary>
    public required int UnlockLevel { get; init; }

    /// <summary>Display name shown to players.</summary>
    public required string Name { get; init; }

    /// <summary>Flavor text describing the ability.</summary>
    public required string Description { get; init; }

    /// <summary>What resource this ability consumes: Focus, Stamina, or Health.</summary>
    public required CostType CostType { get; init; }

    /// <summary>How much of the cost resource to consume on cast.</summary>
    public required int CostValue { get; init; }

    /// <summary>Cooldown duration in pulses (250ms each). 0 = no cooldown.</summary>
    public required long CooldownPulses { get; init; }

    /// <summary>Time before effect lands, in pulses. Null or 0 = instant cast.</summary>
    public long? CastTimePulses { get; init; }

    /// <summary>Who or what can be targeted: self, single target, or AoE.</summary>
    public required TargetingType TargetingType { get; init; }

    /// <summary>
    /// What this ability does, in order. One entry is the ordinary case; several let a single
    /// ability do several things.
    /// </summary>
    /// <remarks>
    /// <b>A list because one slot could not describe half of what a Path wants.</b> Last Stand is
    /// the example that forced it: it should raise the Warden's maximum health and harden their
    /// defence, and with a single effect it had to be authored as a heal — a different thing that
    /// happened to keep them alive for a moment.
    ///
    /// Applied in the order given, and every one of them lands: this is a list of things the
    /// ability does, not a set of alternatives. Cost and cooldown are still charged once, for the
    /// ability rather than per effect, because what a player spends is the ability.
    ///
    /// A concrete <c>List</c> rather than <c>IReadOnlyList</c> because Npgsql maps this straight to
    /// jsonb as a POCO, the way a mob's <c>Attacks</c> already is, and an interface gives it
    /// nothing to materialise into.
    /// </remarks>
    public required List<AbilityEffectSpec> Effects { get; init; }
}

/// <summary>One effect an ability applies, and the parameters that shape it.</summary>
/// <param name="Key">
/// The executor to run, as <c>EffectRegistry</c> knows it: <c>damage.physical</c>,
/// <c>heal.restore</c>.
/// </param>
/// <param name="Params">
/// Read by name and by the executor alone. Anything it does not recognise is skipped in silence,
/// which is why <see cref="AbilityValidator"/> checks the keys rather than trusting them.
/// </param>
/// <remarks>
/// <b>One constructor, deliberately.</b> A convenience overload taking just a key was here, and it
/// made the record undeserialisable: System.Text.Json refuses a type with two parameterised
/// constructors and no <c>[JsonConstructor]</c>, so every ability read back from jsonb threw. The
/// column round-trips through that serialiser, so a second constructor is not a convenience worth
/// having — write the empty dictionary at the call site if an executor ever needs no parameters.
/// </remarks>
public sealed record AbilityEffectSpec(string Key, Dictionary<string, string> Params);

/// <summary>Resource consumed by an ability.</summary>
public enum CostType
{
    Focus = 0,
    Stamina = 1,
    Health = 2,
}

/// <summary>Targeting mode for an ability.</summary>
public enum TargetingType
{
    /// <summary>Ability must target one entity (player specifies).</summary>
    SingleTarget = 0,

    /// <summary>Ability targets the caster only.</summary>
    Self = 1,

    /// <summary>Ability targets all occupants in the room (filters per target).</summary>
    Aoe = 2,
}
