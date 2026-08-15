using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Abilities;
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

        var legend = room.HasGrid ? room.Legend : new Dictionary<string, string>(StringComparer.Ordinal) { ["."] = "floor" };

        actor.Send(new OutboundEvent(
            EventTypes.Map,
            _layout.BuildMap(room, occupants, mobs, items, actor)));

        actor.Send(new OutboundEvent(
            EventTypes.Contents,
            BuildContents(occupants, mobs, items, actor, legend)));

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
        var legend = room.HasGrid ? room.Legend : new Dictionary<string, string>(StringComparer.Ordinal) { ["."] = "floor" };

        foreach (var viewer in occupants)
        {
            viewer.Send(new OutboundEvent(EventTypes.Map, _layout.BuildMap(room, occupants, mobs, items, viewer)));
            viewer.Send(new OutboundEvent(EventTypes.Contents, BuildContents(occupants, mobs, items, viewer, legend, contents)));
        }
    }

    /// <summary>
    /// Sends the character's whole ability list, each with whatever is left of its cooldown.
    /// </summary>
    /// <remarks>
    /// Sent on entering the world and whenever the set could have changed - a level-up grants
    /// abilities, and a builder editing one changes what the rest of them say. Not sent per pulse:
    /// the client counts the cooldowns down itself and only needs correcting when it has been
    /// away, which is exactly when this arrives (see <see cref="AbilitiesPayload"/>).
    ///
    /// A cache-less host sends an empty list rather than throwing. That is a host that never
    /// loaded abilities, which is a test harness rather than a game.
    /// </remarks>
    public static void SendAbilities(
        PlayerActor actor,
        WorldState world,
        AbilityCache? cache,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(world);

        var character = actor.Character;

        var known = cache is null
            ? []
            : cache.All.Values
                .Where(a => a.Path == character.Path && a.UnlockLevel <= character.Level)
                .OrderBy(a => a.UnlockLevel)
                .ThenBy(a => a.Key, StringComparer.Ordinal)
                .ToList();

        var entries = known
            .Select(a => new AbilityEntry(
                a.Key,
                a.Name,
                // What a player actually types. `cast` refuses a skill (§4.7), so telling the
                // client which word to use is the difference between a panel that teaches the
                // vocabulary and one that lists keys.
                AbilityKinds.VerbFor(a),
                a.CostType.ToString(),
                a.CostValue,
                a.CooldownPulses,
                RemainingCooldown(world, character.Id, a, currentPulse),
                AbilityKinds.Of(a) == AbilityKind.Spell))
            .ToList();

        actor.LastSentAbilityLevel = character.Level;
        actor.Send(new OutboundEvent(EventTypes.Abilities, new AbilitiesPayload(entries)));
    }

    /// <summary>
    /// Resends the roster when the character has levelled since the last one.
    /// </summary>
    /// <remarks>
    /// Levelling is what changes the set, and it happens in three places. Comparing the level once
    /// per pulse costs an int and cannot be forgotten by a new one, which is the same trade
    /// <see cref="SendVitalsIfChanged"/> makes.
    ///
    /// A builder retuning an ability is deliberately *not* covered: that changes what the panel
    /// says rather than which abilities exist, and the corrected roster arrives the next time the
    /// character enters. Pushing it would mean the applier reaching every player of a Path on
    /// every save.
    /// </remarks>
    public static void SendAbilitiesIfLevelled(
        PlayerActor actor,
        WorldState world,
        AbilityCache? cache,
        long currentPulse)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.LastSentAbilityLevel == actor.Character.Level)
        {
            return;
        }

        actor.LastSentAbilityLevel = actor.Character.Level;
        SendAbilities(actor, world, cache, currentPulse);
    }

    /// <summary>Pulses left before this ability may be used again. Zero when it is ready.</summary>
    private static long RemainingCooldown(
        WorldState world,
        Guid characterId,
        Ability ability,
        long currentPulse)
    {
        if (world.GetAbilityCooldown(characterId, ability.Key) is not { } startedAt)
        {
            return 0;
        }

        var remaining = (startedAt + ability.CooldownPulses) - currentPulse;
        return remaining > 0 ? remaining : 0;
    }

    /// <summary>
    /// Names whatever the levels just crossed granted, one line each.
    /// </summary>
    /// <remarks>
    /// <b>Nothing else tells the player.</b> The roster is a panel, and the panel now shows only
    /// what is cooling - so an ability that arrives without a line here arrives without anything a
    /// player would notice. Sent to the transcript rather than pushed at the panel because that is
    /// where the game speaks.
    ///
    /// Takes the level the character started at rather than the number of levels gained: a single
    /// award can carry someone across several at once, and everything unlocked on the way is still
    /// theirs to be told about.
    ///
    /// A null actor is a character nobody is playing, and a null cache is a host that never loaded
    /// abilities - both send nothing rather than throwing, which is the same trade
    /// <see cref="SendAbilities"/> makes.
    /// </remarks>
    public static void SendUnlocks(PlayerActor? actor, AbilityCache? cache, int fromLevel)
    {
        if (actor is null || actor.Character.Level <= fromLevel)
        {
            return;
        }

        var lines = LevelUpUnlocks.Announce(
            cache?.All.Values ?? [],
            actor.Character.Path,
            fromLevel,
            actor.Character.Level);

        foreach (var line in lines)
        {
            actor.SendText(line, "levelup");
        }
    }

    /// <summary>Tells one player that an ability of theirs has started cooling down.</summary>
    public static void SendCooldown(PlayerActor actor, string abilityKey, long pulses)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.Send(new OutboundEvent(EventTypes.Cooldown, new CooldownPayload(abilityKey, pulses)));
    }

    public static void SendVitals(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var payload = VitalsOf(actor);
        actor.LastSentVitals = payload;
        actor.Send(new OutboundEvent(EventTypes.Vitals, payload));
    }

    /// <summary>
    /// Sends vitals only when they differ from the last frame this player received.
    /// </summary>
    /// <remarks>
    /// Combat resolves every pulse, so an unconditional push would be four frames a second per
    /// fighter. Comparing the payload rather than tracking a dirty flag means damage, healing,
    /// regeneration, experience and levelling are all covered by construction - there is no
    /// mutation site that can forget to mark itself.
    /// </remarks>
    public static void SendVitalsIfChanged(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var payload = VitalsOf(actor);
        if (payload == actor.LastSentVitals)
        {
            return;
        }

        actor.LastSentVitals = payload;
        actor.Send(new OutboundEvent(EventTypes.Vitals, payload));
    }

    private static VitalsPayload VitalsOf(PlayerActor actor)
    {
        var c = actor.Character;
        var v = c.Vitals;

        return new VitalsPayload(
            v.Health, v.HealthMax,
            v.Focus, v.FocusMax,
            v.Stamina, v.StaminaMax,
            c.Level, c.Xp, c.Path.ToString(),
            c.Gold);
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
            var displayName = mob.DisplayName;
            var prose = NarrationHelper.BuildSentence(displayName, "is here.");
            spans.Add(new TextSpan($"\n{prose}", "mob"));
        }

        foreach (var item in items.OrderBy(i => i.TemplateKey))
        {
            var displayName = item.DisplayName;
            spans.Add(new TextSpan($"\nYou see {NarrationHelper.WithArticle(displayName)}.", "item"));
        }

        actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static ContentsPayload BuildContents(
        IReadOnlyList<PlayerActor> occupants,
        IReadOnlyList<Mob> mobs,
        IReadOnlyList<ItemInstance> items,
        PlayerActor viewer,
        IReadOnlyDictionary<string, string>? legend = null,
        (List<ContentEntry> Occupants, List<ContentEntry> Items)? prebuilt = null)
    {
        var (occupantEntries, itemEntries) = prebuilt ?? BuildContentsFor(occupants, mobs, items);

        // The viewer is shown as "you" to match how they appear on the map.
        var adjusted = occupantEntries
            .Select(e => e.Keyword == viewer.Name.ToLowerInvariant()
                ? e with { Icon = "@", Label = "you" }
                : e)
            .ToList();

        return new ContentsPayload(adjusted, itemEntries, legend);
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
            .Select(m => {
                var displayName = m.DisplayName;
                var icon = displayName[0].ToString();
                return new ContentEntry(icon, displayName, m.TemplateKey.ToLowerInvariant());
            }));

        // Add items
        itemEntries.AddRange(items
            .OrderBy(i => i.TemplateKey)
            .Select(i => {
                var displayName = i.DisplayName;
                return new ContentEntry(i.Icon, displayName, i.TemplateKey.ToLowerInvariant());
            }));

        return (occupantEntries, itemEntries);
    }
}
