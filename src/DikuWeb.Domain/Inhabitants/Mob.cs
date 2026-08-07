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

    /// <summary>Level, unchanged from template (not stat-adjusted).</summary>
    public int Level { get; set; }

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
