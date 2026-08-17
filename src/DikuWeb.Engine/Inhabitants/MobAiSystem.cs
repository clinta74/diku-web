using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Characters;
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
        TryEmote(world, roomKey, mob, template, pulse);

        // Wander: pick a random exit and move (respects noMob flag, sentinel flag)
        if (ShouldWander(world, mob, template, room, pulse))
        {
            TryWander(world, roomKey, mob, room, template);
        }

        // Aggression: attack valid targets in the room
        if (ShouldAggress(mob, template, roomKey, world))
        {
            TryAggress(world, roomKey, mob, template);
        }
    }

    /// <summary>
    /// Says whichever line is due, if any (PLAN.md §4.8).
    /// </summary>
    /// <remarks>
    /// <b>Each line keeps its own clock.</b> This used to be one interval shared by every mob in
    /// the game — sixteen pulses, four seconds — which meant a shopkeeper's sales pitch and a
    /// rat's squeak arrived at exactly the same rate, and anything with an emote list said
    /// something roughly fifteen times a minute.
    ///
    /// A fresh mob is scheduled rather than fired, so nothing greets the room the instant it
    /// spawns, and a spawner filling three slots does not produce three lines at once.
    ///
    /// <b>One line per tick at most</b>, the most overdue first. Two mobs answering each other on
    /// the same pulse is atmosphere; one mob saying two things at once is a glitch.
    /// </remarks>
    private void TryEmote(WorldState world, RoomKey roomKey, Mob mob, MobTemplate template, long pulse)
    {
        var emotes = MobBehavior.EmoteScheduleOf(template.Behavior);
        if (emotes.Count == 0)
        {
            return;
        }

        var schedule = MobState.EmoteScheduleIn(mob);
        MobEmote? due = null;
        var dueAt = long.MaxValue;

        foreach (var emote in emotes)
        {
            if (!schedule.TryGetValue(emote.Text, out var next))
            {
                // First sight of this line - for a new mob, or one whose template just gained it.
                schedule[emote.Text] = emote.NextPulseAfter(pulse, random);
                continue;
            }

            if (next <= pulse && next < dueAt)
            {
                due = emote;
                dueAt = next;
            }
        }

        if (due is not null)
        {
            foreach (var player in world.OccupantsOf(roomKey))
            {
                // A sleeping player gets none of this. Idle flavour is the bulk of what arrives in
                // a quiet room, and arriving while asleep it is just noise on a screen nobody is
                // reading - DreamSystem sends something every five minutes instead. The line is
                // still *scheduled* below whether or not anyone was awake for it, because the
                // room's clock is not the sleeper's business.
                if (player.Character.RestState == CharacterRestState.Sleep)
                {
                    continue;
                }

                // MobLabel, not template.Name. Every other narration path in the game - the room
                // listing, examine, combat, death - disambiguates two of a kind as "a terrace crow
                // (2)", and the AI lines are the ones a player reads most often (BUGS.md #13).
                player.SendText(
                    NarrationHelper.BuildSentence(
                        MobLabel.For(world.MobsIn(roomKey), mob), due.Text),
                    "mob-action");
            }

            schedule[due.Text] = due.NextPulseAfter(pulse, random);
        }

        // Written back with the lines the template no longer carries dropped, so renaming an
        // emote in the builder does not leave the old text accruing in every mob's state forever.
        MobState.SetEmoteSchedule(mob, schedule, emotes.Select(e => e.Text));
    }

    private bool ShouldWander(WorldState world, Mob mob, MobTemplate template, Room room, long pulse)
    {
        // A fight is a claim on a mob's attention, and something that strolls out of one reads as
        // combat having done nothing - the same argument the stun guard in Tick already makes for
        // itself. Reported from live play: "You begin attacking a zombie!" and, two swings later,
        // "A zombie leaves north." The fight was over before it had started.
        //
        // Not a substitute for a mob deciding to run away. Fleeing is a decision, narrated as one,
        // and it would end the fight properly; this is a mob strolling off mid-swing because its
        // wander timer happened to come due.
        if (mob.CombatState == CombatState.Fighting)
        {
            return false;
        }

        // Standing still is the default, so this asks for permission rather than for a veto. The
        // answer was resolved once at spawn from the template and its spawner (PLAN.md §4.8) -
        // reading it back here rather than re-deciding keeps a mob's behaviour stable for its
        // whole life, the way its resolved stats are.
        if (!GetMobStateBool(mob, MobBehavior.WandersKey))
        {
            return false;
        }

        // Through IsFlagSet, which resolves room -> zone -> world -> default (PLAN.md §4.10).
        // This read the room's own flags directly, so `noMob` set on a zone showed as "on, from
        // zone" in the builder and was ignored by the AI - the only flag reader in the engine not
        // going through the hierarchy (BUGS.md #18).
        if (world.IsFlagSet(room.Key, RoomFlags.NoMob))
        {
            return false;
        }

        return ClaimWanderTurn(mob, template, pulse);
    }

    /// <summary>
    /// True when this mob's turn to wander has come, re-arming its timer either way.
    /// </summary>
    /// <remarks>
    /// <b>A schedule per mob, not one interval everybody reads.</b> This used to be
    /// <c>pulse - lastWanderPulse >= interval</c> against a counter starting at zero, which put the
    /// whole world in step: a fresh mob is already overdue, so it moves on the first sweep that
    /// sees it and re-arms from that pulse - and the AI itself only runs every sixteen pulses, so
    /// the comparison quantises to the same sweeps for everyone. Two rats spawned into one room
    /// left together and arrived together for as long as they both lived, which reads as a squad
    /// rather than as two animals.
    ///
    /// So the fix <see cref="MobEmote"/> already made for idle lines, applied here: a fresh mob is
    /// scheduled rather than fired, and each turn is drawn from a range around the authored
    /// interval instead of from the interval itself. The template's number keeps its meaning as
    /// the cadence - it is the average of one now rather than the exact period - so nothing
    /// authored has to change.
    ///
    /// Re-armed whether or not a move follows. A mob fenced in by <c>noMob</c> neighbours or by
    /// its own zone border would otherwise be due on every sweep for the rest of its life,
    /// rescanning its exits to conclude again that it cannot use any of them.
    /// </remarks>
    private bool ClaimWanderTurn(Mob mob, MobTemplate template, long pulse)
    {
        if (MobState.WanderNextOf(mob) is not { } due)
        {
            ScheduleWander(mob, template, pulse);
            return false;
        }

        // Only a turn that came re-arms. Redrawing on every sweep would push the deadline ahead
        // of the sweep that was about to meet it, so the mob would move only when the draw landed
        // inside one sweep's worth of pulses - a cadence set by how often the AI runs rather than
        // by the number the template authored.
        if (due > pulse)
        {
            return false;
        }

        ScheduleWander(mob, template, pulse);
        return true;
    }

    /// <summary>
    /// Draws the next wander pulse from a range centred on the template's interval.
    /// </summary>
    /// <remarks>
    /// Half the interval either side: wide enough that two mobs sharing a template drift apart
    /// within a few moves, narrow enough that one authored to move every six seconds is still
    /// recognisably doing that. The floor of one pulse is what keeps an interval of zero - or a
    /// spread that rounds down to it - from meaning "every pulse, forever".
    /// </remarks>
    private void ScheduleWander(Mob mob, MobTemplate template, long pulse)
    {
        var spread = template.WanderIntervalPulses / 2;
        var min = Math.Max(1, template.WanderIntervalPulses - spread);
        var max = Math.Max(min, template.WanderIntervalPulses + spread);

        // Exclusive upper bound, hence the +1 - the same reason MobEmote.NextPulseAfter has one.
        MobState.SetWanderNext(mob, pulse + random.Next(min, max + 1));
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
            if (targetRoom is null || world.IsFlagSet(targetRoom.Key, RoomFlags.NoMob))
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

            // A conditional exit is a door, and nothing that wanders opens one (§4.15). Mobs hold
            // neither flags nor inventory, so there is no question to ask them — and the
            // alternative is flagging noMob on every room behind every lock and remembering to do
            // it again each time a builder digs one, which is the failure mode the home-zone rule
            // above exists to avoid.
            if (exit.IsConditional)
            {
                continue;
            }

            // Valid destination found: move the mob
            // Labelled against the room it is leaving, and then against the room it arrives in -
            // the ordinal is positional, so the crow that was (2) where it came from may well be
            // (1) where it lands, and each room's watchers should read their own.
            var leaveProse = NarrationHelper.BuildSentence(
                MobLabel.For(world.MobsIn(fromRoomKey), mob),
                $"leaves {exit.Direction.ToLowerName()}");

            // Notify in source room with direction
            var fromOccupants = world.OccupantsOf(fromRoomKey);
            foreach (var player in fromOccupants)
            {
                player.SendText(leaveProse, "movement");
            }

            // Move the mob
            world.MoveMob(mob, exit.ToRoomKey);

            // Built after the move, because the ordinal is positional: what this crow is called in
            // the room it just entered depends on what was already standing there.
            var arriveProse = NarrationHelper.BuildSentence(
                MobLabel.For(world.MobsIn(exit.ToRoomKey), mob),
                $"arrives from the {exit.Direction.Opposite().ToLowerName()}");

            // Notify in destination room
            var toOccupants = world.OccupantsOf(exit.ToRoomKey);
            foreach (var player in toOccupants)
            {
                player.SendText(arriveProse, "movement");
            }

            // Update map for both rooms
            view.RefreshRoom(world, fromRoomKey);
            view.RefreshRoom(world, exit.ToRoomKey);

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

        var label = MobLabel.For(world.MobsIn(roomKey), mob);

        target.SendText(
            NarrationHelper.BuildSentence(label, "attacks you!"), "combat");
        foreach (var other in world.OccupantsOf(roomKey))
        {
            if (other.CharacterId != target.CharacterId)
            {
                other.SendText(
                    NarrationHelper.BuildSentence(label, $"attacks {target.Name}!"),
                    "combat");
            }
        }
    }

    /// <summary>
    /// Reads a flag out of the mob's state bag.
    /// </summary>
    /// <remarks>
    /// Through <see cref="JsonBag"/> rather than by pattern-matching <c>is bool</c>. Nothing reads
    /// or writes the <c>mobs</c> table today, so the bag only ever holds the native types this
    /// file put there - but the table and the <c>DbSet&lt;Mob&gt;</c> both exist, and the day mob
    /// persistence lands every value comes back as a <c>JsonElement</c>. At that point a type
    /// check would silently return false, and the sentinel flag would go quiet with nothing to
    /// show for it. That has already happened three times to the other bags; this is the same
    /// trap, disarmed early. <see cref="MobState"/> reads its own keys the same way.
    /// </remarks>
    private static bool GetMobStateBool(Mob mob, string key) =>
        JsonBag.Boolean(mob.State, key);
}
