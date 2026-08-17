namespace DikuWeb.Domain.Spawning;

/// <summary>
/// PLAN.md §4.8: A declarative population-maintenance rule. "Keep N instances of template T
/// alive in rooms R, respawn D seconds after each dies or is picked up."
/// Spawners are global; templates are global. One spawner can maintain mobs or items.
/// </summary>
public sealed class Spawner
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Which zone owns this spawner rule.</summary>
    public required string ZoneKey { get; init; }

    /// <summary>Which template to spawn: ItemTemplate or MobTemplate key.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>What kind of template: Item or Mob.</summary>
    public TemplateKind TemplateKind { get; init; }

    /// <summary>Rooms where this spawner places instances. Defaults to all rooms in zone if empty.</summary>
    public List<string> RoomKeys { get; set; } = new();

    /// <summary>Target population.</summary>
    /// <remarks>
    /// It used to say "before spawn density multiplier". There was no spawn density multiplier -
    /// the dial existed and was applied by nothing, and has since been deleted (BUGS.md #17).
    /// </remarks>
    public int TargetCount { get; set; } = 1;

    // `RespawnSeconds` used to sit here, defaulting to 30 and offered as a number field in the
    // builder. `SpawnerSystem` refills straight to TargetCount on its own 15-second sweep and
    // never read it, so a builder who typed "respawn after 600 seconds" got 15. Deleted rather
    // than implemented, for the reason the two multipliers were: staggered respawn is a design
    // decision about pacing, and reintroducing it should start from that rather than from a
    // number somebody typed into a field that did nothing (BUGS.md #17).

    /// <summary>
    /// Whether mobs from this spawner wander. <b>Null defers to the template</b>, which is the
    /// default and the usual answer; true and false override it for these mobs only.
    /// </summary>
    /// <remarks>
    /// Three-valued because the template now carries the default (PLAN.md §4.8) and the spawner
    /// still has the last word. Two values could not express *"whatever this mob normally does"*,
    /// so every spawner had to restate a decision that belongs on the thing being spawned — and
    /// restating it is how a shopkeeper placed by a second spawner ends up strolling out of its
    /// own shop.
    ///
    /// <b>Named for what it permits, not for what it forbids.</b> This was <c>Sentinel</c>, whose
    /// polarity was the opposite of the template's <c>wanders</c> key — and the one thing this
    /// codebase has inverted before is a pair of flags that mean the same thing in opposite
    /// directions (HISTORY.md, 5.1e: every "weaken" in the game made its target harder to kill).
    /// One direction, both places, so the resolution reads <c>spawner.Wanders ?? template</c>.
    /// </remarks>
    public bool? Wanders { get; set; }

    /// <summary>
    /// The level mobs from this spawner fight at. <b>Null lets the zone decide</b>, which is the
    /// default and the usual answer; a value pins the level whatever the zone's dials say.
    /// </summary>
    /// <remarks>
    /// <b>Because a zone dial is zone-wide, and a zone is not uniform.</b> Scaling a 25–30 zone by
    /// two turns a level-10 template into the level-20 content you wanted <em>and</em> a level-25
    /// template already written for that zone into a level-50 monster. Without a per-placement say,
    /// the only ways out are to author every template at its final level — losing the reuse §4.4
    /// exists for — or to leave the dials at 1.0 and hand-write a template per tier.
    ///
    /// <b>It states an outcome, not a factor.</b> "Fights at 27" is what a builder means; a
    /// multiplier is what they would have to compute to say it. The factor falls out —
    /// <c>N / templateLevel</c>, applied wherever <c>Strength</c> is applied
    /// (<see cref="Inhabitants.MobScaling.FromTarget"/>).
    ///
    /// <b>It replaces the zone's combat dials rather than composing with them</b>, world dials
    /// included. Composing would turn 27 in a doubled zone into 54, and the number a builder typed
    /// would be a lie — which discards the entire reason for stating outcomes.
    ///
    /// Nullable for the reason <see cref="Wanders"/> is: null is a third answer, not a missing one.
    /// Meaningless on an item spawner, which the builder API refuses — an item has no level, and a
    /// stored value would go live the day someone flips the kind to Mob.
    /// </remarks>
    public int? FightsAtLevel { get; set; }
}

/// <summary>What kind of thing a spawner creates.</summary>
public enum TemplateKind
{
    Item,
    Mob,
}
