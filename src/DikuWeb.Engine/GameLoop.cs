using System.Diagnostics;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Commands;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Quests;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DikuWeb.Engine;

/// <summary>
/// The single thread that owns all mutable world state (PLAN.md §2.1).
///
/// Nothing else mutates <see cref="WorldState"/>. Every input arrives through
/// <see cref="GameGateway"/>, is drained here, and produces output events and save jobs.
/// That is what removes locks from the game logic entirely and makes ticks replayable.
/// </summary>
public sealed class GameLoop(
    GameGateway gateway,
    WorldState world,
    CommandRegistry commands,
    PlayerView view,
    WorldMutationApplier applier,
    LoopWorldEditor loopEditor,
    IAccountAdminQueue adminQueue,
    SystemGameClock clock,
    IWorldSource worldSource,
    ICharacterSaveQueue saveQueue,
    IItemSaveQueue itemSaveQueue,
    IAbilityRepository? abilityRepository,
    AbilityCache? abilityCache,
    IQuestRepository? questRepository,
    QuestCache? questCache,
    IItemTemplateRepository? itemTemplateRepository,
    ItemTemplateCache? itemTemplateCache,
    IMobTemplateRepository? mobTemplateRepository,
    MobTemplateCache? mobTemplateCache,
    SpawnerSystem? spawnerSystem,
    MobAiSystem? mobAiSystem,
    CombatSystem? combatSystem,
    AbilitySystem? abilitySystem,
    EngineOptions options,
    ILogger<GameLoop> logger) : BackgroundService
{
    /// <summary>Bounded per pulse so a command flood cannot starve scheduled systems.</summary>
    private const int MaxCommandsPerPulse = 512;

    private const int LinkDeadCheckPulses = 16;

    public WorldState World => world;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var data = await worldSource.LoadAsync(stoppingToken);
        world.Load(data.Worlds, data.Zones, data.Rooms);

        // Load ability cache for synchronous lookups during command handling
        if (abilityRepository != null && abilityCache != null)
        {
            await abilityCache.LoadAsync(abilityRepository, stoppingToken);
        }

        // Load quest cache for synchronous lookups during quest commands
        if (questRepository != null && questCache != null)
        {
            await questCache.LoadAsync(questRepository, stoppingToken);
        }

        // Load item template cache for synchronous lookups during quest rewards
        if (itemTemplateRepository != null && itemTemplateCache != null)
        {
            await itemTemplateCache.LoadAsync(itemTemplateRepository, stoppingToken);
        }

        // Load mob template cache for synchronous lookups during shop commands
        if (mobTemplateRepository != null && mobTemplateCache != null)
        {
            await mobTemplateCache.LoadAsync(mobTemplateRepository, stoppingToken);
        }

        EngineLog.LoopStarting(logger, world.RoomCount, GameTiming.PulseInterval.TotalMilliseconds);

        using var timer = new PeriodicTimer(GameTiming.PulseInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var start = Stopwatch.GetTimestamp();

                try
                {
                    Pulse();
                }
                catch (Exception ex)
                {
                    // The loop must survive anything a handler throws. A dead loop is a dead
                    // world for every connected player, so this catch is deliberately broad.
                    EngineLog.PulseFailed(logger, clock.CurrentPulse, ex);
                }

                var elapsed = Stopwatch.GetElapsedTime(start);
                if (elapsed > GameTiming.PulseBudget)
                {
                    EngineLog.SlowPulse(
                        logger,
                        clock.CurrentPulse,
                        elapsed.TotalMilliseconds,
                        GameTiming.PulseBudget.TotalMilliseconds);
                }

                clock.Advance();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        ShutdownAllPlayers();
        gateway.Complete();
        EngineLog.LoopStopped(logger, clock.CurrentPulse);
    }

    private void Pulse()
    {
        DrainInbound();

        var pulse = clock.CurrentPulse;

        if (GameTiming.RunsOn(pulse, LinkDeadCheckPulses))
        {
            ExpireLinkDeadPlayers();
        }

        if (spawnerSystem != null && GameTiming.RunsOn(pulse, GameTiming.SpawnSweepPulses))
        {
            // Fire and forget - spawner runs on thread pool, never blocks the loop
            _ = spawnerSystem.RunAsync(world, CancellationToken.None);
        }

        if (mobAiSystem != null && GameTiming.RunsOn(pulse, GameTiming.MobAiPulses))
        {
            // Fire and forget - mob AI runs on thread pool for template lookups, but updates
            // are applied on the loop thread via SendText and RefreshRoom
            _ = mobAiSystem.RunAsync(world, CancellationToken.None);
        }

        if (abilitySystem != null)
        {
            // Run every pulse - checks for cast-time resolution and interruption
            abilitySystem.Tick(world);
        }

        if (GameTiming.RunsOn(pulse, GameTiming.RegenPulses))
        {
            RegenSystem.Tick(world);
            EffectExpirySystem.Tick(world, pulse);

            // Send updated vitals to all players after regeneration
            foreach (var actor in world.AllPlayers)
            {
                PlayerView.SendVitals(actor);
            }
        }

        if (combatSystem != null && GameTiming.RunsOn(pulse, CombatSystem.TickIntervalPulses))
        {
            // Fire and forget - combat runs on thread pool for template/item lookups
            // PlayerView is passed for room refreshes after mob death or loot spawning
            _ = combatSystem.Tick(world, CancellationToken.None);

            // Send updated vitals to all combatants after combat resolves
            foreach (var combat in world.AllCombats.Where(c => c.Combatants.Count > 0))
            {
                foreach (var combatantId in combat.Combatants)
                {
                    if (EntityId.IsCharacter(combatantId))
                    {
                        var charId = EntityId.ToGuid(combatantId);
                        var actor = world.FindByCharacter(charId);
                        if (actor != null)
                        {
                            PlayerView.SendVitals(actor);
                        }
                    }
                }
            }
        }

        if (pulse > 0 && GameTiming.RunsOn(pulse, GameTiming.AutosavePulses))
        {
            Autosave();
        }
    }

    private void DrainInbound()
    {
        var handled = 0;

        while (handled < MaxCommandsPerPulse && gateway.Reader.TryRead(out var message))
        {
            handled++;

            switch (message)
            {
                case EnterWorld enter:
                    HandleEnter(enter);
                    break;
                case PlayerCommand command:
                    HandleCommand(command);
                    break;
                case LeaveWorld leave:
                    HandleLeave(leave);
                    break;
                case WorldMutation mutation:
                    HandleMutation(mutation);
                    break;
                case ReplaceWorld replace:
                    HandleReplaceWorld(replace);
                    break;
                case Notify notify:
                    world.FindBySession(notify.SessionId)?.SendSys(notify.Message, notify.Kind);
                    break;
                case SetActorRole role:
                    HandleSetActorRole(role);
                    break;
            }
        }
    }

    /// <summary>
    /// Applies a role change to every character this account has in the world (PLAN.md §7.7).
    /// </summary>
    private void HandleSetActorRole(SetActorRole message)
    {
        foreach (var actor in world.AllPlayers.Where(p => p.Character.AccountId == message.AccountId))
        {
            if (actor.Role == message.Role)
            {
                continue;
            }

            actor.Role = message.Role;

            // Told plainly, because it changes what their commands do. Silently revoking the
            // builder verbs would read as the game breaking.
            actor.SendSys(
                actor.IsBuilder
                    ? "You have been granted building privileges."
                    : "Your building privileges have been removed.",
                SysKinds.Warning);

            EngineLog.ActorRoleChanged(logger, actor.Name, message.Role.ToString());
        }
    }

    /// <summary>
    /// Applies a builder edit and answers the waiting HTTP request (PLAN.md §7.3).
    /// </summary>
    private void HandleMutation(WorldMutation message)
    {
        try
        {
            var result = applier.Apply(message.Change);

            if (result.Success)
            {
                EngineLog.MutationApplied(
                    logger, message.Change.EntityKind, message.Change.EntityKey, result.Applied.Count);
            }
            else
            {
                EngineLog.MutationRefused(
                    logger, message.Change.EntityKind, message.Change.EntityKey, result.Message ?? "");
            }

            message.Completion.TrySetResult(result);
        }
        catch (Exception ex)
        {
            // The applier is written not to throw, so reaching here is a bug rather than a
            // refusal. Fail the request instead of the loop, and make sure the builder's
            // request does not hang waiting for a completion that never arrives.
            EngineLog.MutationFailed(logger, message.Change.EntityKind, message.Change.EntityKey, ex);
            message.Completion.TrySetResult(
                MutationResult.Fail(MutationError.Invalid, "The edit could not be applied."));
        }
    }

    private void HandleReplaceWorld(ReplaceWorld message)
    {
        try
        {
            world.Load(message.Data.Worlds, message.Data.Zones, message.Data.Rooms);

            // A reload can remove the room someone is standing in. Same treatment as a
            // deleted room (PLAN.md §7.4): move them somewhere real, never leave them nowhere.
            foreach (var actor in world.AllPlayers.ToList())
            {
                if (world.FindRoom(actor.RoomKey) is null)
                {
                    actor.SendSys("The world shifted around you.", SysKinds.Warning);
                    world.Move(actor, options.StartingRoom);
                }

                view.SendRoom(world, actor, verbose: false);
            }

            EngineLog.WorldReloaded(logger, world.RoomCount);
            message.Completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            EngineLog.WorldReloadFailed(logger, ex);
            message.Completion.TrySetResult(false);
        }
    }

    // -----------------------------------------------------------------------
    // Message handling
    // -----------------------------------------------------------------------

    private void HandleEnter(EnterWorld message)
    {
        var existing = world.FindByCharacter(message.Character.Id);

        if (existing is not null)
        {
            // Same character, new stream. Either a reconnect inside the grace window or a
            // second tab; both resolve to rebinding rather than cloning the character.
            var wasLinkDead = existing.IsLinkDead;

            existing.Output?.TryComplete();
            world.Rebind(existing, message.SessionId);
            existing.Output = message.Output;
            existing.LinkDeadSincePulse = 0;

            // Reload quests in case they changed while link-dead
            if (message.Quests.Count > 0)
            {
                world.LoadCharacterQuests(existing.Character.Id, message.Quests);
            }

            // Reload items in case inventory changed while link-dead
            if (message.Items.Count > 0)
            {
                world.LoadCharacterItems(existing.Character.Id, message.Items);
            }

            if (wasLinkDead)
            {
                existing.SendSys("Reconnected.", SysKinds.Info);
                EngineLog.PlayerReconnected(logger, existing.Name);
            }

            PlayerView.SendVitals(existing);
            view.SendRoom(world, existing, verbose: true);
            view.RefreshRoom(world, existing.RoomKey);
            return;
        }

        var character = message.Character;

        // The saved room may have been deleted by a builder while the player was away.
        if (!world.TryGetRoom(character.RoomKey, out _))
        {
            EngineLog.RelocatedFromMissingRoom(
                logger,
                character.Name,
                character.RoomKey.ToString(),
                options.StartingRoom.ToString());

            character.RoomKey = options.StartingRoom;
        }

        var actor = new PlayerActor
        {
            Character = character,
            Role = message.Role,
            SessionId = message.SessionId,
            Output = message.Output,
        };

        world.Add(actor);

        // Load character's quests from the message
        if (message.Quests.Count > 0)
        {
            world.LoadCharacterQuests(character.Id, message.Quests);
        }

        // Load character's inventory and equipped items from the message
        if (message.Items.Count > 0)
        {
            world.LoadCharacterItems(character.Id, message.Items);
        }

        actor.SendSys($"Welcome to Aldenmoor, {actor.Name}.", SysKinds.Info);
        PlayerView.SendVitals(actor);
        view.SendRoom(world, actor, verbose: true);

        foreach (var other in world.OthersIn(actor.RoomKey, actor))
        {
            other.SendText($"{actor.Name} appears.", "movement");
        }

        view.RefreshRoom(world, actor.RoomKey);
        EngineLog.PlayerEntered(logger, actor.Name, actor.RoomKey.ToString());
    }

    private void HandleCommand(PlayerCommand message)
    {
        var actor = world.FindBySession(message.SessionId);
        if (actor is null)
        {
            return;
        }

        var (verb, argument) = CommandRegistry.Split(message.Input);
        if (verb.Length == 0)
        {
            return;
        }

        var definition = commands.Find(verb);
        if (definition is null)
        {
            actor.SendText($"'{verb}' is not something you can do. Try 'help'.", "bad");
            return;
        }

        var context = new CommandContext
        {
            Actor = actor,
            World = world,
            View = view,
            Editor = loopEditor,
            AdminQueue = adminQueue,
            ItemSaveQueue = itemSaveQueue,
            ItemTemplates = itemTemplateCache,
            Verb = verb,
            Argument = argument,
        };

        try
        {
            definition.Handler(context);
        }
        catch (Exception ex)
        {
            // One player's bad command must not take down everyone else's world.
            EngineLog.CommandFailed(logger, message.Input, actor.Name, ex);
            actor.SendText("Something went wrong with that command.", "bad");
            return;
        }

        // Refresh any rooms marked for update by the command handler
        foreach (var roomKey in context.RoomsToRefresh)
        {
            view.RefreshRoom(world, roomKey);
        }

        if (context.LeaveRequested is { } reason)
        {
            RemovePlayer(actor, reason);
        }
    }

    private void HandleLeave(LeaveWorld message)
    {
        var actor = world.FindBySession(message.SessionId);
        if (actor is null)
        {
            return;
        }

        if (message.Reason == LeaveReason.LinkDead)
        {
            // PLAN.md §3.6: the character stays in the world for the grace window and can
            // still be attacked. Classic MUD risk, and it makes a flaky connection survivable.
            actor.Output?.TryComplete();
            actor.Output = null;
            actor.LinkDeadSincePulse = clock.CurrentPulse;

            foreach (var other in world.OthersIn(actor.RoomKey, actor))
            {
                other.SendText($"{actor.Name} goes still, eyes unfocused.", "movement");
            }

            view.RefreshRoom(world, actor.RoomKey);
            return;
        }

        RemovePlayer(actor, message.Reason);
    }

    // -----------------------------------------------------------------------
    // Scheduled systems
    // -----------------------------------------------------------------------

    private void ExpireLinkDeadPlayers()
    {
        var expired = world.AllPlayers
            .Where(p => p.IsLinkDead
                && clock.CurrentPulse - p.LinkDeadSincePulse >= options.LinkDeadGracePulses)
            .ToList();

        foreach (var actor in expired)
        {
            RemovePlayer(actor, LeaveReason.LinkDeadExpired);
        }
    }

    private void Autosave()
    {
        foreach (var actor in world.AllPlayers)
        {
            saveQueue.Enqueue(CharacterSnapshot.From(actor.Character, clock.UtcNow));
        }
    }

    private void ShutdownAllPlayers()
    {
        foreach (var actor in world.AllPlayers.ToList())
        {
            actor.SendSys("The world is closing. Your progress is saved.", SysKinds.Disconnect);
            RemovePlayer(actor, LeaveReason.Shutdown);
        }
    }

    private void RemovePlayer(PlayerActor actor, LeaveReason reason)
    {
        var room = actor.RoomKey;

        saveQueue.Enqueue(CharacterSnapshot.From(actor.Character, clock.UtcNow));
        world.Remove(actor);
        actor.Output?.TryComplete();
        actor.Output = null;

        if (reason == LeaveReason.LinkDeadExpired)
        {
            foreach (var other in world.OccupantsOf(room))
            {
                other.SendText($"{actor.Name} fades away.", "movement");
            }
        }

        view.RefreshRoom(world, room);
        EngineLog.PlayerLeft(logger, actor.Name, reason.ToString());
    }
}
