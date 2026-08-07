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
    PlayerView view)
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

    private async Task ProcessMobAsync(WorldState world, Mob mob, CancellationToken ct)
    {
        var template = await templates.GetByKeyAsync(mob.TemplateKey, ct);
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
        if (!template.Behavior.TryGetValue("emotes", out var emotesObj) || emotesObj is not List<object> emotes || emotes.Count == 0)
        {
            return false;
        }

        var lastEmotePulse = GetMobStateLong(mob, "lastEmotePulse");
        return pulse - lastEmotePulse >= EmoteIntervalPulses;
    }

    private void EmitEmote(WorldState world, RoomKey roomKey, Mob mob, MobTemplate template)
    {
        if (!template.Behavior.TryGetValue("emotes", out var emotesObj) || emotesObj is not List<object> emotes || emotes.Count == 0)
        {
            return;
        }

        var emoteIdx = random.Next(0, emotes.Count);
        var emote = emotes[emoteIdx].ToString() ?? "emotes mysteriously";

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

        // Check if mob is aggressive in its behavior
        if (!template.Behavior.TryGetValue("type", out var typeObj) || typeObj?.ToString() != "aggressive")
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

    private long GetMobStateLong(Mob mob, string key)
    {
        if (mob.State.TryGetValue(key, out var val))
        {
            return val switch
            {
                long l => l,
                int i => i,
                _ => 0,
            };
        }
        return 0;
    }

    private void SetMobStateLong(Mob mob, string key, long value)
    {
        mob.State[key] = value;
    }

    private bool GetMobStateBool(Mob mob, string key)
    {
        if (mob.State.TryGetValue(key, out var val))
        {
            return val is true or "true" or 1;
        }
        return false;
    }
}
