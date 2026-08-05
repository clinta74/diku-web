using System.Diagnostics;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Commands;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
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
    SystemGameClock clock,
    IWorldSource worldSource,
    ICharacterSaveQueue saveQueue,
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
            }
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
            SessionId = message.SessionId,
            Output = message.Output,
        };

        world.Add(actor);

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
