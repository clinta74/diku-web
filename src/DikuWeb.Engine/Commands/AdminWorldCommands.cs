using System.Globalization;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Admin commands the loop can answer for itself (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="AdminCommands"/>, which is about the <em>account</em>
/// store: those cannot do their own work, because §2.1 forbids the loop a database call, so each
/// hands off to a queue and is answered later. Everything here is about the world, which the loop
/// owns outright — so these answer immediately, and mixing the two would make it impossible to
/// tell from the file which kind of command you were reading.
///
/// <c>ban</c> and <c>mute</c> are therefore not here: they outlive the session, so they belong
/// with the account work.
/// </remarks>
internal static class AdminWorldCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        // "tele": tell takes "te", and talk takes a bare "t".
        commands.Add(new CommandDefinition(
            "teleport", 4, "teleport <name> - pull a player to your room (admin)",
            Teleport, Requires: AccountRole.Admin));

        // "stat": the player-facing `stats` screen demands five characters, so four is free.
        commands.Add(new CommandDefinition(
            "stat", 4, "stat [name] - inspect a character, mob, or item (admin)",
            Stat, Requires: AccountRole.Admin));

        // Full word. Disconnecting somebody by fumbling a prefix of `kill` would be a bad
        // surprise for both of you.
        commands.Add(new CommandDefinition(
            "kick", 4, "kick <name> [reason] - disconnect a player (admin)",
            Kick, Requires: AccountRole.Admin));

        // All eight characters, for the same reason `quit` demands four: there is no undo, and
        // this one ends everybody's session rather than just your own.
        commands.Add(new CommandDefinition(
            "shutdown", 8,
            "shutdown <minutes|now|cancel> [reason] - close the world, with warning (admin)",
            Shutdown, Requires: AccountRole.Admin));

        commands.Add(new CommandDefinition(
            "set", 3, "set <name> <field> <value> - change a live character value (admin)",
            Set, Requires: AccountRole.Admin));
    }

    /// <summary>
    /// The fields <c>set</c> will write, and what each one means.
    /// </summary>
    /// <remarks>
    /// A closed list rather than reflection over <see cref="Domain.Characters.Character"/>. Every
    /// field here is one somebody decided a support tool should be able to change; reflection
    /// would silently expose every field added afterwards, including the ones where writing a
    /// value directly corrupts something else — setting <c>RoomKey</c> without moving the actor
    /// would leave a character indexed in a room they are not in.
    /// </remarks>
    private static readonly string[] _settableFields = ["health", "focus", "stamina", "gold", "xp", "level"];

    /// <summary>
    /// Changes one live value on a character.
    /// </summary>
    /// <remarks>
    /// The blunt instrument, and deliberately so: it is what you reach for when a bug has left
    /// somebody with the wrong number and the alternative is SQL against a running world — which
    /// would be invisible to the loop, since the character in memory is the authority until it
    /// saves over the top of whatever was written.
    ///
    /// Changes are made through the world's own objects, so the save queue picks them up on the
    /// next autosave exactly as any other change would.
    /// </remarks>
    private static void Set(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        var parts = ctx.Argument.Trim().Split(' ', 3, StringSplitOptions.TrimEntries);

        if (parts.Length < 3)
        {
            ctx.Reply($"Usage: set <name> <field> <value>. Fields: {string.Join(", ", _settableFields)}.", "bad");
            return;
        }

        var target = ctx.World.FindPlayerByName(parts[0])
            ?? NameMatch.Best(ctx.World.AllPlayers, parts[0], p => p.Name, _ => null);

        if (target is null)
        {
            ctx.Reply($"Nobody named '{parts[0]}' is online.", "bad");
            return;
        }

        var field = parts[1].ToLowerInvariant();

        // The field is checked before the value, so an unknown field reports itself rather than
        // complaining about a value that was never going to be used.
        if (!_settableFields.Contains(field, StringComparer.Ordinal))
        {
            ctx.Reply($"'{field}' is not settable. Fields: {string.Join(", ", _settableFields)}.", "bad");
            return;
        }

        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value < 0)
        {
            ctx.Reply("The value must be a whole number, zero or more.", "bad");
            return;
        }

        var character = target.Character;
        var vitals = character.Vitals;

        // Reported before and after, because the point of using this is that a number was wrong -
        // and an admin who cannot see what it was cannot tell whether they fixed it.
        string before;

        switch (field)
        {
            case "health":
                before = vitals.Health.ToString(CultureInfo.InvariantCulture);
                // Clamped to the maximum rather than refused: an admin topping someone up types a
                // big number on purpose, and a full heal should not need arithmetic.
                vitals.Health = Math.Min(value, vitals.HealthMax);
                break;

            case "focus":
                before = vitals.Focus.ToString(CultureInfo.InvariantCulture);
                vitals.Focus = Math.Min(value, vitals.FocusMax);
                break;

            case "stamina":
                before = vitals.Stamina.ToString(CultureInfo.InvariantCulture);
                vitals.Stamina = Math.Min(value, vitals.StaminaMax);
                break;

            case "gold":
                before = character.Gold.ToString(CultureInfo.InvariantCulture);
                character.Gold = value;
                break;

            case "xp":
                before = character.Xp.ToString(CultureInfo.InvariantCulture);
                character.Xp = value;
                break;

            case "level":
                // Not clamped to the xp curve. Levelling somebody to test content is the reason
                // this exists, and refusing it because their experience does not agree would make
                // the tool useless for the job it is for.
                before = character.Level.ToString(CultureInfo.InvariantCulture);
                character.Level = Math.Max(1, value);
                break;

            default:
                // Unreachable while the switch covers _settableFields, which the guard above has
                // already checked against. Kept so adding a name to that list without a case here
                // fails loudly rather than silently doing nothing.
                ctx.Reply($"'{field}' is listed as settable but nothing here writes it.", "bad");
                return;
        }

        ctx.Reply($"{target.Name}: {field} {before} → {value}.", "heading");
        target.SendSys($"An administrator set your {field} to {value}.", SysKinds.Warning);
    }

    /// <summary>
    /// Closes the world after a warning.
    /// </summary>
    /// <remarks>
    /// Progress is already safe without this — characters autosave and are saved again on the way
    /// out (§11). What the warning protects is the half hour somebody spent walking to a boss,
    /// which no save gives back.
    ///
    /// The delay is in <b>minutes</b>, because that is the unit the decision is made in. Seconds
    /// would make the common case <c>shutdown 600</c>, which is a number nobody wants to be
    /// converting under pressure — and a mistyped one is ten times too long or ten times too
    /// short.
    ///
    /// Immediate shutdown is spelled <c>now</c> rather than <c>0</c>, so the destructive case is a
    /// word typed on purpose and never the result of a fumbled digit.
    /// </remarks>
    private static void Shutdown(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        if (ctx.Shutdown is null)
        {
            ctx.Reply("This server cannot be closed from in here.", "bad");
            return;
        }

        var parts = ctx.Argument.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
        var when = parts[0];
        var reason = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;

        if (when.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (!ctx.Shutdown.Cancel())
            {
                ctx.Reply("No shutdown is scheduled.", "bad");
                return;
            }

            foreach (var actor in ctx.World.AllPlayers)
            {
                actor.SendSys("The shutdown is called off. Carry on.", SysKinds.Info);
            }

            return;
        }

        if (when.Equals("now", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Shutdown.Schedule(0, reason);
            ctx.Reply("Closing the world now.", "heading");
            return;
        }

        if (when.Length == 0)
        {
            // Asking with no argument reports rather than acts, so the way to find out whether a
            // shutdown is pending is not to schedule one.
            ctx.Reply(
                ctx.Shutdown.SecondsRemaining is { } remaining
                    ? $"The world closes in {ShutdownSchedule.Describe(remaining)}."
                    : "No shutdown is scheduled. Use 'shutdown <minutes>', 'shutdown now', or 'shutdown cancel'.",
                ctx.Shutdown.IsScheduled ? "heading" : "bad");
            return;
        }

        if (!int.TryParse(when, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            minutes < 0)
        {
            ctx.Reply("Usage: shutdown <minutes>, shutdown now, or shutdown cancel.", "bad");
            return;
        }

        ctx.Shutdown.Schedule(minutes * 60, reason);
        ctx.Reply($"The world closes in {ShutdownSchedule.Describe(minutes * 60)}.", "heading");
    }

    /// <summary>
    /// Brings a player to the admin.
    /// </summary>
    /// <remarks>
    /// One direction only. <c>goto</c> already takes a staff member to a room, so the missing
    /// half was fetching someone — which is what you actually want when answering "I am stuck".
    ///
    /// Goes through <see cref="Travel"/> for the movement but not for
    /// <see cref="Travel.Refuse"/>: being held by <c>noRecall</c> or a root is a common reason to
    /// need fetching, and an admin tool that the content could veto would be no use in the case it
    /// exists for. A fight is not an exception either — pulling someone out of one is a moderation
    /// action, and the combat system drops a combatant who is no longer in the room.
    /// </remarks>
    private static void Teleport(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        if (!ctx.HasArgument)
        {
            ctx.Reply("Usage: teleport <name>", "bad");
            return;
        }

        var name = ctx.Argument.Trim().Split(' ')[0];

        var target = NameMatch.Best(
            ctx.World.AllPlayers.Where(p => p.CharacterId != ctx.Actor.CharacterId),
            name,
            p => p.Name,
            _ => null);

        if (target is null)
        {
            ctx.Reply($"Nobody named '{name}' is online.", "bad");
            return;
        }

        if (target.RoomKey == ctx.Actor.RoomKey)
        {
            ctx.Reply($"{target.Name} is already here.", "bad");
            return;
        }

        if (!ctx.World.TryGetRoom(ctx.Actor.RoomKey, out var destination))
        {
            ctx.Reply("You are not standing anywhere that exists.", "bad");
            return;
        }

        Travel.To(
            ctx.World,
            ctx.View,
            target,
            destination,
            $"{target.Name} is taken away.",
            $"{target.Name} arrives, looking startled.");

        target.SendText("The world folds around you, and someone else decides where.", "heading");
        ctx.Reply($"You pull {target.Name} here.", "heading");
    }

    /// <summary>
    /// Dumps what the game actually knows about something, as opposed to what it shows.
    /// </summary>
    /// <remarks>
    /// The point is the gap between the two. A player reports that a mob hits too hard; the room
    /// description does not say what its resolved damage is, which zone's multipliers produced it,
    /// or which spawner is responsible for it still being there. This does.
    /// </remarks>
    private static void Stat(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        // No argument means yourself, which is the quickest way to check that a multiplier or a
        // level-up did what it was supposed to.
        if (!ctx.HasArgument)
        {
            StatCharacter(ctx, ctx.Actor);
            return;
        }

        var name = ctx.Argument.Trim();
        var room = ctx.Actor.RoomKey;

        // Room first, then the world. Standing in front of something is the usual reason to be
        // asking about it, and a global name search would make "stat rat" ambiguous everywhere.
        if (NameMatch.Best(ctx.World.OccupantsOf(room), name, p => p.Name, _ => null) is { } here)
        {
            StatCharacter(ctx, here);
            return;
        }

        if (NameMatch.Best(ctx.World.MobsIn(room), name, m => m.TemplateName, m => m.TemplateKey) is { } mob)
        {
            StatMob(ctx, mob);
            return;
        }

        var items = ctx.World.ItemsIn(room).Concat(ctx.World.InventoryOf(ctx.Actor.CharacterId));

        if (NameMatch.Best(items, name, i => i.TemplateName, i => i.TemplateKey) is { } item)
        {
            StatItem(ctx, item);
            return;
        }

        if (ctx.World.FindPlayerByName(name) is { } elsewhere)
        {
            StatCharacter(ctx, elsewhere);
            return;
        }

        ctx.Reply($"Nothing here answers to '{name}'.", "bad");
    }

    private static void StatCharacter(CommandContext ctx, PlayerActor actor)
    {
        var character = actor.Character;
        var vitals = character.Vitals;
        var party = ctx.World.Parties.Of(character.Id);

        ctx.Reply($"{character.Name}  ({actor.EntityId})", "heading");
        ctx.Reply($"  role      {actor.Role}{(actor.IsLinkDead ? "  [link-dead]" : string.Empty)}");
        ctx.Reply($"  level     {character.Level} {character.Path}   xp {character.Xp}   gold {character.Gold}");
        ctx.Reply($"  health    {vitals.Health}/{vitals.HealthMax}   focus {vitals.Focus}/{vitals.FocusMax}   stamina {vitals.Stamina}/{vitals.StaminaMax}");
        ctx.Reply($"  where     {character.RoomKey}   {character.RestState}   {character.CombatState}");
        ctx.Reply($"  bound     {character.RespawnRoomKey?.ToString() ?? "nowhere"}");
        var attributes = character.Attributes;
        ctx.Reply(
            $"  attrs     might {attributes.Might}  agility {attributes.Agility}  " +
            $"vitality {attributes.Vitality}  insight {attributes.Insight}  resolve {attributes.Resolve}");

        if (character.CurrentTarget is { } target)
        {
            ctx.Reply($"  fighting  {target}");
        }

        if (party is not null)
        {
            var names = party.Members
                .Select(id => ctx.World.FindByCharacter(id)?.Name ?? id.ToString())
                .ToList();

            ctx.Reply($"  group     {string.Join(", ", names)}");
        }

        Effects(ctx, character.Id);
    }

    private static void StatMob(CommandContext ctx, Domain.Inhabitants.Mob mob)
    {
        var vitals = mob.Vitals;

        ctx.Reply($"{mob.TemplateName}  ({EntityId.ForMob(mob.Id)})", "heading");
        ctx.Reply($"  template  {mob.TemplateKey}   level {mob.Level}");
        ctx.Reply($"  health    {vitals.Health}/{vitals.HealthMax}");
        ctx.Reply($"  where     {mob.RoomKey}   {mob.CombatState}");
        ctx.Reply($"  rewards   {mob.ResolvedXp} xp   {mob.ResolvedGold} gold  (after multipliers)");

        // The two questions the playtesting notes were about: why is this still here, and why is
        // it standing where it is.
        ctx.Reply($"  spawner   {mob.SpawnerId?.ToString() ?? "none - placed by hand"}");
        ctx.Reply($"  home      {MobState.HomeZoneOf(mob) ?? "unrecorded"}");

        if (mob.CurrentTarget is { } target)
        {
            ctx.Reply($"  fighting  {target}");
        }

        ctx.Reply($"  stats     {Bag(mob.ResolvedStats)}");
        ctx.Reply($"  spawn ×   {string.Join(", ", mob.SpawnMultipliers.Select(m => $"{m.Key} {m.Value.ToString("0.##", CultureInfo.InvariantCulture)}"))}");

        Effects(ctx, mob.Id);
    }

    private static void StatItem(CommandContext ctx, Domain.Items.ItemInstance item)
    {
        ctx.Reply($"{item.TemplateName}  ({item.Id})", "heading");
        ctx.Reply($"  template  {item.TemplateKey}   value {item.Value}");
        ctx.Reply($"  slot      {item.EquippedSlot?.ToString() ?? "not equipped"}");
        ctx.Reply($"  where     {item.RoomKey ?? $"carried by {item.OwnerCharacterId?.ToString() ?? "nobody"}"}");
        ctx.Reply($"  spawner   {item.SpawnerId?.ToString() ?? "none - placed by hand"}");
        ctx.Reply($"  stats     {Bag(item.ResolvedStats)}");
        ctx.Reply($"  state     {Bag(item.State)}");
    }

    private static void Effects(CommandContext ctx, Guid entityId)
    {
        var effects = ctx.World.GetActiveEffects(entityId);
        if (effects.Count == 0)
        {
            return;
        }

        var now = ctx.Clock?.CurrentPulse ?? 0L;

        foreach (var effect in effects)
        {
            ctx.Reply(
                $"  effect    {effect.EffectKey} '{effect.Name}'  " +
                $"{effect.ExpiresAtPulse - now} pulses left  from {effect.SourceEntityId}");
        }
    }

    /// <summary>Renders a free-form bag on one line, or says plainly that it is empty.</summary>
    private static string Bag(IReadOnlyDictionary<string, object>? bag) =>
        bag is null || bag.Count == 0
            ? "(empty)"
            : string.Join("  ", bag.Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>
    /// Disconnects a player.
    /// </summary>
    /// <remarks>
    /// The removal goes back through the loop rather than happening here, because leaving the
    /// world is more than dropping out of <see cref="WorldState"/> — it saves, closes the outbound
    /// channel, and redraws the room. A handler doing that itself would be a second copy of the
    /// list, and the copy is what goes stale.
    ///
    /// Their character is saved on the way out, exactly as a <c>quit</c> would. A kick is a way to
    /// end a session, not a punishment applied to a character sheet.
    /// </remarks>
    private static void Kick(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        if (!ctx.HasArgument)
        {
            ctx.Reply("Usage: kick <name> [reason]", "bad");
            return;
        }

        var parts = ctx.Argument.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
        var name = parts[0];
        var reason = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;

        var target = NameMatch.Best(
            ctx.World.AllPlayers.Where(p => p.CharacterId != ctx.Actor.CharacterId),
            name,
            p => p.Name,
            _ => null);

        if (target is null)
        {
            // Deliberately not "you cannot kick yourself" as a special case: an admin who wants to
            // leave can type quit, and excluding themselves from the search is the whole rule.
            ctx.Reply($"Nobody named '{name}' is online.", "bad");
            return;
        }

        target.SendSys(
            reason is null
                ? "You have been disconnected by an administrator."
                : $"You have been disconnected by an administrator: {reason}",
            SysKinds.Disconnect);

        // Said in the room as well. A character vanishing with no explanation reads as a bug to
        // everyone who was standing there.
        foreach (var other in ctx.World.OthersIn(target.RoomKey, target))
        {
            other.SendText($"{target.Name} is removed from the world.", "movement");
        }

        ctx.RequestRemoval(target.CharacterId, LeaveReason.Kicked);
        ctx.Reply($"You disconnect {target.Name}.", "heading");
    }

    /// <summary>
    /// Shared with <see cref="AdminCommands"/> rather than copied. Two gates that agree today is
    /// how you end up with two gates that do not.
    /// </summary>
    private static bool RequireAdmin(CommandContext ctx) => AdminCommands.RequireAdmin(ctx);
}
