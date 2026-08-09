using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Inhabitants;

/// <summary>
/// Runs every 4 seconds (16 pulses). Handles mob behavioral state machine:
/// idle (emoting), wandering (room-to-room), and readiness for combat.
/// Runs on the game loop thread; directly modifies world state and sends events to players.
/// </summary>
public sealed class MobAiSystem(
    IMobTemplateRepository templates,
    IRandomSource random,
    IGameClock clock,
    PlayerView view,
    MobTemplateCache? templateCache = null)
{
    private const int EmoteIntervalPulses = 16;  // Every 4 seconds

    public async Task RunAsync(WorldState world, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(world);

        var allMobs = world.AllMobs.ToList();
        foreach (var mob in allMobs)
        {
            await ProcessMobAsync(world, mob, ct);
        }
    }

    /// <summary>
    /// The template this mob was spawned from, from memory where possible.
    /// </summary>
    /// <remarks>
    /// This used to be an unconditional <c>GetByKeyAsync</c>, which opens a fresh
    /// <c>DbContext</c> and issues one <c>SELECT</c> per mob. At one sweep every four seconds
    /// that is a world with twenty mobs making twenty round-trips a sweep with nobody logged in -
    /// throughput rather than correctness, which is why nothing surfaced it until the SQL log was
    /// read. The cache holds every template, is loaded at boot, and is kept live by the applier;
    /// this system simply predates it.
    ///
    /// The fallback is gated on <see cref="MobTemplateCache.IsLoaded"/> rather than on a miss.
    /// Falling back per miss would look safer and behave worse: a mob whose template a builder
    /// deleted misses forever, so it would issue a doomed query for that mob on every sweep for
    /// as long as it stands there - reintroducing the exact pathology being removed, in the one
    /// case where it never pays off. An unloaded cache is a different thing, and reading through
    /// to the repository is what keeps a host that never called <c>LoadAsync</c> from silently
    /// having no mob AI at all.
    /// </remarks>
    private async Task<MobTemplate?> TemplateForAsync(Mob mob, CancellationToken ct) =>
        templateCache is { IsLoaded: true }
            ? templateCache.Get(mob.TemplateKey)
            : await templates.GetByKeyAsync(mob.TemplateKey, ct);

    private async Task ProcessMobAsync(WorldState world, Mob mob, CancellationToken ct)
    {
        var template = await TemplateForAsync(mob, ct);
        if (template is null)
        {
            return;
        }

        var roomKey = RoomKey.Parse(mob.RoomKey);
        var room = world.FindRoom(roomKey);
        if (room is null)
        {
            return;
        }

        var pulse = clock.CurrentPulse;

        // A stunned mob does nothing at all - it does not emote, wander off, or pick a fight.
        // Gating only its swings would have it strolling out of the room mid-stun, which reads
        // as the stun having done nothing.
        if (world.IsStunned(mob.Id, pulse))
        {
            return;
        }

        // Idle: emit expressive actions from behavior.emotes
        if (ShouldEmote(mob, pulse, template))
        {
            EmitEmote(world, roomKey, mob, template);
        }

        // Wander: pick a random exit and move (respects noMob flag, sentinel flag)
        if (ShouldWander(mob, template, room, pulse))
        {
            TryWander(world, roomKey, mob, room, template);
        }

        // Aggression: attack valid targets in the room
        if (ShouldAggress(mob, template, roomKey, world))
        {
            TryAggress(world, roomKey, mob, template);
        }
    }

    private bool ShouldEmote(Mob mob, long pulse, MobTemplate template)
    {
        if (MobBehavior.EmotesOf(template.Behavior).Count == 0)
        {
            return false;
        }

        var lastEmotePulse = GetMobStateLong(mob, "lastEmotePulse");
        return pulse - lastEmotePulse >= EmoteIntervalPulses;
    }

    private void EmitEmote(WorldState world, RoomKey roomKey, Mob mob, MobTemplate template)
    {
        var emotes = MobBehavior.EmotesOf(template.Behavior);
        if (emotes.Count == 0)
        {
            return;
        }

        var emote = emotes[random.Next(0, emotes.Count)];

        // Notify occupants
        var occupants = world.OccupantsOf(roomKey);
        foreach (var player in occupants)
        {
            player.SendText($"{template.Name} {emote}.", "mob-action");
        }

        SetMobStateLong(mob, "lastEmotePulse", clock.CurrentPulse);
    }

    private bool ShouldWander(Mob mob, MobTemplate template, Room room, long pulse)
    {
        // Check sentinel flag: some mobs don't wander
        if (GetMobStateBool(mob, "sentinel"))
        {
            return false;
        }

        // Check room noMob flag: this room forbids wandering mobs
        if (room.Flags.BooleanOrNull(RoomFlags.NoMob.Key) == true)
        {
            return false;
        }

        var lastWanderPulse = GetMobStateLong(mob, "lastWanderPulse");
        return pulse - lastWanderPulse >= template.WanderIntervalPulses;
    }

    private void TryWander(WorldState world, RoomKey fromRoomKey, Mob mob, Room room, MobTemplate template)
    {
        if (room.Exits.Count == 0)
        {
            return;
        }

        // Where this mob is allowed to end up. Null only when the template says it roams — a mob
        // with no recorded home falls back to the zone it is standing in, because absence has to
        // resolve to the confining value the way an absent room flag resolves to the safe one.
        // Otherwise every mob written before the key existed would be set loose on the next boot.
        var homeZone = MobBehavior.Roams(template.Behavior)
            ? null
            : MobState.HomeZoneOf(mob) ?? RoomKey.Parse(mob.RoomKey).ZoneKey;

        // Pick a random exit, respecting noMob flag in the destination
        var exits = room.Exits.ToList();
        var startIdx = random.Next(0, exits.Count);

        for (var i = 0; i < exits.Count; i++)
        {
            var exit = exits[(startIdx + i) % exits.Count];
            var targetRoom = world.FindRoom(exit.ToRoomKey);
            if (targetRoom is null || targetRoom.Flags.BooleanOrNull(RoomFlags.NoMob.Key) == true)
            {
                continue;
            }

            // Both conditions have to hold: noMob says "not into this room", the home zone says
            // "not out of that zone". An exit that crosses the border is skipped rather than
            // ending the attempt, so a mob standing at the edge still wanders inward.
            if (homeZone is not null &&
                !string.Equals(exit.ToRoomKey.ZoneKey, homeZone, StringComparison.Ordinal))
            {
                continue;
            }

            // Valid destination found: move the mob
            var leaveProse = NarrationHelper.BuildSentence(template.Name, $"leaves {exit.Direction.ToLowerName()}");
            var arriveProse = NarrationHelper.BuildSentence(template.Name, $"arrives from the {exit.Direction.Opposite().ToLowerName()}");

            // Notify in source room with direction
            var fromOccupants = world.OccupantsOf(fromRoomKey);
            foreach (var player in fromOccupants)
            {
                player.SendText(leaveProse, "movement");
            }

            // Move the mob
            world.MoveMob(mob, exit.ToRoomKey);

            // Notify in destination room
            var toOccupants = world.OccupantsOf(exit.ToRoomKey);
            foreach (var player in toOccupants)
            {
                player.SendText(arriveProse, "movement");
            }

            // Update map for both rooms
            view.RefreshRoom(world, fromRoomKey);
            view.RefreshRoom(world, exit.ToRoomKey);

            SetMobStateLong(mob, "lastWanderPulse", clock.CurrentPulse);
            return;
        }
    }

    private bool ShouldAggress(Mob mob, MobTemplate template, RoomKey roomKey, WorldState world)
    {
        // Check if mob is already in combat
        if (mob.CombatState == CombatState.Fighting)
        {
            return false;
        }

        // Only an aggressive mob starts fights. This also covers the NPC case, which is the
        // point: a shopkeeper must not open combat with the customer.
        if (!MobBehavior.IsAggressive(template.Behavior))
        {
            return false;
        }

        // Check if room is peaceful (forbids all combat)
        if (world.IsFlagSet(roomKey, RoomFlags.Peaceful))
        {
            return false;
        }

        // At least one player must be in the room
        return world.OccupantsOf(roomKey).Count > 0;
    }

    private void TryAggress(WorldState world, RoomKey roomKey, Mob mob, MobTemplate template)
    {
        // Find first valid player target
        var occupants = world.OccupantsOf(roomKey).ToList();
        var target = occupants.FirstOrDefault();
        if (target == null)
        {
            return;
        }

        // Initiate combat
        var targetId = EntityId.ForCharacter(target.CharacterId);
        var mobId = EntityId.ForMob(mob.Id);

        var combat = world.GetOrCreateCombat(roomKey);
        combat.AddCombatant(mobId);
        combat.AddCombatant(targetId);

        // Seed hate list so GetTopHater works on first tick
        combat.AddToHateList(mobId, targetId, 1);

        mob.CombatState = CombatState.Fighting;
        mob.CurrentTarget = targetId;

        target.Character.CombatState = CombatState.Fighting;
        // Note: Don't set target.Character.CurrentTarget — player must `kill` to fight back

        target.SendText($"{template.Name} attacks you!", "combat");
        foreach (var other in world.OccupantsOf(roomKey))
        {
            if (other.CharacterId != target.CharacterId)
            {
                other.SendText($"{template.Name} attacks {target.Name}!", "combat");
            }
        }
    }

    /// <summary>
    /// Reads a pulse counter out of the mob's state bag.
    /// </summary>
    /// <remarks>
    /// Through <see cref="JsonBag"/> rather than by pattern-matching <c>is long</c>. Nothing reads
    /// or writes the <c>mobs</c> table today, so the bag only ever holds the native types this
    /// file put there - but the table and the <c>DbSet&lt;Mob&gt;</c> both exist, and the day mob
    /// persistence lands every value comes back as a <c>JsonElement</c>. At that point a type
    /// check would silently return 0 here and false below, and the emote timers and the sentinel
    /// flag would go quiet with nothing to show for it. That has already happened three times to
    /// the other bags; this is the same trap, disarmed early.
    /// </remarks>
    private static long GetMobStateLong(Mob mob, string key) =>
        JsonBag.Int64(mob.State, key);

    private static void SetMobStateLong(Mob mob, string key, long value) =>
        mob.State[key] = value;

    private static bool GetMobStateBool(Mob mob, string key) =>
        JsonBag.Boolean(mob.State, key);
}
