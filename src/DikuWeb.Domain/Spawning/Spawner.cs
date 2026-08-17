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

    /// <summary>
    /// Seconds to wait after a loss before replacing one of this spawner's instances.
    /// </summary>
    /// <remarks>
    /// <b>This is how rare a thing is.</b> A boss that should be an event rather than a rotation
    /// sets minutes or hours here; a patch of herbs that exists to be picked leaves the default.
    /// It is on the spawner rather than the template because rarity is a property of the
    /// *placement*: the same chassis can be a nuisance in one zone and the only one of its kind in
    /// another, which is the same argument <see cref="TargetCount"/> and <see cref="FightsAtLevel"/>
    /// already make.
    ///
    /// <b>Sixty, not thirty.</b> The old default was 30 and read by nothing at all, so every
    /// spawner in the game actually refilled on the sweep's own 15-second cadence — which meant a
    /// player could stand in one room and kill the same mob forever rather than going to look for
    /// another (BUGS.md #17, and PLAN.md §4.8 for what the sweep does with this).
    ///
    /// <b>One replacement per window, not a refill to target.</b> A cleared room of four comes back
    /// over four windows, so clearing it buys real time rather than fifteen seconds.
    ///
    /// Resolution is the sweep's, so the real delay is this plus up to 15 seconds. That is
    /// immaterial at a minute and invisible at an hour.
    /// </remarks>
    public int RespawnSeconds { get; set; } = 60;

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
