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
}
