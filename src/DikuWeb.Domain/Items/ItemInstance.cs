namespace DikuWeb.Domain.Items;

/// <summary>
/// PLAN.md §4.8: A runtime instance of an ItemTemplate, with multiplier-resolved stats.
/// Location is exactly one of: owner inventory, container, ground room, or equipped on a character.
/// Never mutated in-place; state transitions via WorldMutation (picked up, dropped, etc.).
/// </summary>
public sealed class ItemInstance
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Which template was this spawned from.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>Which spawner created this item (for proper population tracking).</summary>
    public Guid? SpawnerId { get; init; }

    /// <summary>Display name from template at spawn time (cached for consistency).</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Icon from template at spawn time (cached for map display).</summary>
    public string Icon { get; init; } = "$";

    /// <summary>Stats after multiplier resolution: damage, armor, bonuses, etc.</summary>
    public Dictionary<string, object> ResolvedStats { get; set; } = new();

    /// <summary>Snapshot of world.mult × zone.mult applied at spawn, for debugging and replay.</summary>
    public Dictionary<string, decimal> SpawnMultipliers { get; set; } = new();

    /// <summary>Current value after multipliers.</summary>
    public int Value { get; set; }

    /// <summary>Owner's character ID if in inventory/equipped. Null if on ground or in container.</summary>
    public Guid? OwnerCharacterId { get; set; }

    /// <summary>Parent container if inside another item. Null if ground item or owned.</summary>
    public Guid? ContainerItemId { get; set; }

    /// <summary>Room this item is on the ground in. Null if owned or contained.</summary>
    public string? RoomKey { get; set; }

    /// <summary>Slot this is equipped in, if any. Null if ground, inventory, or container.</summary>
    public ItemSlot? EquippedSlot { get; set; }

    /// <summary>Free-form state for future features (durability, charges, etc.).</summary>
    public Dictionary<string, object> State { get; set; } = new();
}
