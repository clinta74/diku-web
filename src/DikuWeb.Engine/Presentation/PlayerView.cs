using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Presentation;

/// <summary>
/// Turns world state into the events a client renders. Sits between the command handlers and
/// <see cref="RoomLayoutService"/> so handlers never touch coordinates themselves.
/// </summary>
public sealed class PlayerView(RoomLayoutService layout)
{
    private readonly RoomLayoutService _layout =
        layout ?? throw new ArgumentNullException(nameof(layout));

    /// <summary>
    /// Sends the structured panels plus the prose. Both, always: PLAN.md §5 makes the
    /// scrollback authoritative, so a player who ignores the map must miss nothing.
    /// </summary>
    public void SendRoom(WorldState world, PlayerActor actor, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(actor);

        var room = world.FindRoom(actor.RoomKey);
        if (room is null)
        {
            // The builder deleted this room out from under the player (PLAN.md §7.4).
            actor.SendText("You are nowhere at all. The world has forgotten this place.", "bad");
            return;
        }

        var occupants = world.OccupantsOf(actor.RoomKey);
        var mobs = world.MobsIn(actor.RoomKey);
        var items = world.ItemsIn(actor.RoomKey);
        var exits = room.Exits
            .OrderBy(e => DirectionExtensions.All.ToList().IndexOf(e.Direction))
            .Select(e => e.Direction.ToLowerName())
            .ToList();

        actor.Send(new OutboundEvent(
            EventTypes.Room,
            new RoomPayload(room.Key.ToString(), room.Title, room.Description, exits)));

        actor.Send(new OutboundEvent(
            EventTypes.Map,
            _layout.BuildMap(room, occupants, mobs, items, actor)));

        actor.Send(new OutboundEvent(
            EventTypes.Contents,
            BuildContents(occupants, mobs, items, actor)));

        SendProse(actor, room, occupants, mobs, items, exits, verbose);
    }

    /// <summary>Refreshes the map and contents for everyone standing in a room.</summary>
    public void RefreshRoom(WorldState world, RoomKey roomKey)
    {
        ArgumentNullException.ThrowIfNull(world);

        var room = world.FindRoom(roomKey);
        if (room is null)
        {
            return;
        }

        var occupants = world.OccupantsOf(roomKey);
        var mobs = world.MobsIn(roomKey);
        var items = world.ItemsIn(roomKey);
        var contents = BuildContentsFor(occupants, mobs, items);

        foreach (var viewer in occupants)
        {
            viewer.Send(new OutboundEvent(EventTypes.Map, _layout.BuildMap(room, occupants, mobs, items, viewer)));
            viewer.Send(new OutboundEvent(EventTypes.Contents, BuildContents(occupants, mobs, items, viewer, contents)));
        }
    }

    public static void SendVitals(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var c = actor.Character;
        var v = c.Vitals;

        actor.Send(new OutboundEvent(
            EventTypes.Vitals,
            new VitalsPayload(
                v.Health, v.HealthMax,
                v.Focus, v.FocusMax,
                v.Stamina, v.StaminaMax,
                c.Level, c.Xp, c.Path.ToString())));
    }

    private static void SendProse(
        PlayerActor actor,
        Room room,
        IReadOnlyList<PlayerActor> occupants,
        IReadOnlyList<Mob> mobs,
        IReadOnlyList<ItemInstance> items,
        IReadOnlyList<string> exits,
        bool verbose)
    {
        var spans = new List<TextSpan> { new(room.Title, "room-title") };

        if (verbose && !string.IsNullOrWhiteSpace(room.Description))
        {
            spans.Add(new TextSpan("\n" + room.Description));
        }

        spans.Add(new TextSpan(
            exits.Count == 0
                ? "\nThere are no obvious exits."
                : $"\nExits: {string.Join(", ", exits)}",
            "exits"));

        foreach (var other in occupants.Where(o => o.CharacterId != actor.CharacterId))
        {
            var suffix = other.IsLinkDead ? " (link-dead)" : string.Empty;
            spans.Add(new TextSpan($"\n{other.Name} is here.{suffix}", "occupant"));
        }

        foreach (var mob in mobs.OrderBy(m => m.TemplateKey))
        {
            spans.Add(new TextSpan($"\n{mob.TemplateKey} is here.", "mob"));
        }

        foreach (var item in items.OrderBy(i => i.TemplateKey))
        {
            spans.Add(new TextSpan($"\nYou see {item.TemplateKey} here.", "item"));
        }

        actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static ContentsPayload BuildContents(
        IReadOnlyList<PlayerActor> occupants,
        IReadOnlyList<Mob> mobs,
        IReadOnlyList<ItemInstance> items,
        PlayerActor viewer,
        (List<ContentEntry> Occupants, List<ContentEntry> Items)? prebuilt = null)
    {
        var (occupantEntries, itemEntries) = prebuilt ?? BuildContentsFor(occupants, mobs, items);

        // The viewer is shown as "you" to match how they appear on the map.
        var adjusted = occupantEntries
            .Select(e => e.Keyword == viewer.Name.ToLowerInvariant()
                ? e with { Icon = "@", Label = "you" }
                : e)
            .ToList();

        return new ContentsPayload(adjusted, itemEntries);
    }

    private static (List<ContentEntry> Occupants, List<ContentEntry> Items) BuildContentsFor(
        IReadOnlyList<PlayerActor> occupants,
        IReadOnlyList<Mob> mobs,
        IReadOnlyList<ItemInstance> items)
    {
        var occupantEntries = new List<ContentEntry>();
        var itemEntries = new List<ContentEntry>();

        // Add players
        occupantEntries.AddRange(occupants
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o => new ContentEntry(
                o.Icon,
                o.IsLinkDead ? $"{o.Name} (link-dead)" : o.Name,
                o.Name.ToLowerInvariant())));

        // Add mobs
        occupantEntries.AddRange(mobs
            .OrderBy(m => m.TemplateKey)
            .Select(m => new ContentEntry(m.TemplateKey, m.TemplateKey, m.TemplateKey.ToLowerInvariant())));

        // Add items
        itemEntries.AddRange(items
            .OrderBy(i => i.TemplateKey)
            .Select(i => new ContentEntry("$", i.TemplateKey, i.TemplateKey.ToLowerInvariant())));

        return (occupantEntries, itemEntries);
    }
}
