using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Domain.Inhabitants;

/// <summary>
/// PLAN.md §4.8: A runtime instance of a MobTemplate with multiplier-resolved stats.
/// Lives in a room, has vitals, can be in combat. Never updated in-place after spawn;
/// state changes via WorldMutation.
/// </summary>
public sealed class Mob
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Which template was this spawned from.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>Which spawner created this mob (for proper population tracking).</summary>
    public Guid? SpawnerId { get; init; }

    /// <summary>Display name from template at spawn time (cached for consistency).</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>
    /// What to call this mob on screen: its name, falling back to its key when the instance
    /// carries none.
    /// </summary>
    /// <remarks>
    /// <b>Here rather than at each call site, because the call sites got it wrong.</b> This
    /// fallback was written out by hand in a dozen places and missed in two of them, so
    /// <c>talk</c> answered "ossara-innkeeper has nothing to say about quests" about a character
    /// the room had just introduced as Corun, who keeps the fire. A key is an authoring
    /// identifier; it should never reach a player except as the last resort this property makes it.
    ///
    /// The fallback still matters: a nameless line is unmatchable by every verb that takes a mob,
    /// so showing the key at least tells the player what to type.
    /// </remarks>
    public string DisplayName =>
        string.IsNullOrEmpty(TemplateName) ? TemplateKey : TemplateName;

    /// <summary>
    /// Single character for the map, from the template at spawn time.
    /// </summary>
    /// <remarks>
    /// <b>The instance carries it for the same reason <see cref="TemplateName"/> does</b>: the map
    /// is drawn from what is standing in the room, not from a template lookup per entity per frame.
    ///
    /// This did not exist until the milestone review, and neither render path had anything to read
    /// — both took <c>DisplayName[0]</c> instead. Every mob name in the Reaches begins with its
    /// article, so the map was a field of lowercase <c>a</c> while 68 templates carried a
    /// deliberate scheme: <c>r</c> vermin, <c>c</c> flyers, <c>d</c> canines, <c>@</c> named NPCs
    /// (BUGS.md #10). Item icons were read correctly the whole time, which is what made the map
    /// look intentional rather than broken.
    ///
    /// The first letter stays as the fallback for an instance built without one, which is the same
    /// trade <see cref="DisplayName"/> makes: something recognisable beats nothing.
    /// </remarks>
    public string Icon { get; init; } = string.Empty;

    /// <summary>What to draw for this mob: its icon, or the first letter of its name.</summary>
    public string MapGlyph =>
        string.IsNullOrEmpty(Icon) ? DisplayName[..1] : Icon[..1];

    /// <summary>Level, unchanged from template (not stat-adjusted).</summary>
    public int Level { get; set; }

    /// <summary>
    /// The level this mob actually fights at, resolved from <see cref="Level"/> and its zone's
    /// multipliers at spawn (<see cref="MobLevel.Effective"/>).
    /// </summary>
    /// <remarks>
    /// <b>This is the level everything player-facing should read</b> — experience, <c>consider</c>,
    /// anything that compares a mob to a person. <see cref="Level"/> is what the builder authored
    /// and stays available for the builder's own views, but a zone doubling a mob's health and
    /// damage has changed the fight without changing that number, so using it to judge the fight
    /// is reading the label instead of the creature.
    ///
    /// Snapshotted at spawn like <see cref="ResolvedXp"/>, so retuning a zone changes what spawns
    /// next rather than re-levelling everything already standing in it. Defaults to zero for a mob
    /// built by hand; readers fall back to <see cref="Level"/> rather than treating that as a real
    /// level.
    /// </remarks>
    public int EffectiveLevel { get; set; }

    /// <summary>Current room this mob is in. No x,y here (PLAN.md §4.2).</summary>
    public required string RoomKey { get; set; }

    /// <summary>Health, damage, attributes after multiplier resolution.</summary>
    public Dictionary<string, object> ResolvedStats { get; set; } = new();

    /// <summary>Snapshot of world.mult × zone.mult applied at spawn, for replay.</summary>
    public Dictionary<string, decimal> SpawnMultipliers { get; set; } = new();

    /// <summary>XP awarded on kill, after multipliers.</summary>
    public int ResolvedXp { get; set; }

    /// <summary>Gold dropped on loot, after multipliers.</summary>
    public int ResolvedGold { get; set; }

    /// <summary>Current vitals: health pool, focus, stamina.</summary>
    public Vitals Vitals { get; set; } = new()
    {
        Health = 0,
        HealthMax = 0,
        Focus = 0,
        FocusMax = 0,
        Stamina = 0,
        StaminaMax = 0,
    };

    /// <summary>Current combat engagement state.</summary>
    public CombatState CombatState { get; set; } = CombatState.Idle;

    /// <summary>Entity ID of current target (character ID or other mob ID).</summary>
    public string? CurrentTarget { get; set; }

    /// <summary>Free-form state: { "inCombat": true, "targetId": "...", "sentinelFlag": false }</summary>
    public Dictionary<string, object> State { get; set; } = new();

    /// <summary>
    /// Leaves a fight alive: idle, no target, and whole again (PLAN.md §4.6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mobs used to keep their wounds for the life of the process.</b> Nothing restored
    /// <see cref="Vitals"/> — <c>RegenSystem</c> iterated players only, ending a fight touched
    /// neither side's health, and the spawner replaces a slot only when its occupant is
    /// <em>dead</em>. So chipping something to 5% and walking away left it at 5% an hour later:
    /// attrition was free and permanent, and a camped room degraded monotonically until a restart
    /// (BUGS.md #25).
    /// </para>
    /// <para>
    /// <b>Here rather than at each call site, for the reason <see cref="DisplayName"/> gives</b> —
    /// the hand-written version was already in two places and would have needed the heal added to
    /// both. A promise kept by remembering to keep it at every exit is the defect class
    /// <c>BUGS.md</c> opens by describing.
    /// </para>
    /// <para>
    /// <b>The heal is guarded on being alive; the idling is not.</b> A mob at zero health is dead or
    /// pending removal, and healing it would resurrect it — quietly, with the corpse already looted.
    /// Clearing its combat state is still right, so the guard wraps the heal alone. In practice it
    /// should never fire, because <c>HandleDeath</c> removes the dead from <c>Combatants</c> before
    /// the end-of-fight sweep looks; it exists so this is safe wherever it gets called from next.
    /// </para>
    /// <para>
    /// <b>No leashing.</b> A mob does not walk home, which is why this is a heal rather than a
    /// journey: <see cref="Mob"/> carries no home room and the wander rules stay as they are.
    /// </para>
    /// </remarks>
    public void Disengage()
    {
        CombatState = CombatState.Idle;
        CurrentTarget = null;

        if (Vitals.Health > 0)
        {
            Vitals.Health = Vitals.HealthMax;
        }
    }
}
