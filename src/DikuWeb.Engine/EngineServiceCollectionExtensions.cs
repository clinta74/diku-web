using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Randomness;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Commands;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Quests;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;
using Microsoft.Extensions.DependencyInjection;

namespace DikuWeb.Engine;

public static class EngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the game loop and everything it owns. The host must also register:
    /// - <see cref="IWorldSource"/> for loading world data (Phase 1)
    /// - <see cref="ICharacterSaveQueue"/> for character persistence (Phase 1)
    /// - <see cref="IMobTemplateRepository"/> for mob templates (Phase 3)
    /// - <see cref="IItemTemplateRepository"/> for item templates (Phase 3)
    /// - <see cref="ISpawnerRepository"/> for spawner configuration (Phase 3)
    /// </summary>
    public static IServiceCollection AddDikuWebEngine(
        this IServiceCollection services,
        Action<EngineOptions>? configure = null)
    {
        var options = new EngineOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SystemGameClock>();
        services.AddSingleton<IGameClock>(sp => sp.GetRequiredService<SystemGameClock>());
        services.AddSingleton<IRandomSource>(sp => new SeededRandomSource(Random.Shared.Next()));

        // Singletons because the world is a single shared object owned by one thread.
        services.AddSingleton<WorldState>();
        services.AddSingleton<CommandRegistry>(sp =>
            new CommandRegistry(
                sp.GetService<AbilityCache>(),
                sp.GetService<QuestCache>(),
                sp.GetService<IMobTemplateRepository>(),
                sp.GetService<IItemTemplateRepository>(),
                sp.GetService<MobSpawner>(),
                sp.GetService<ItemSpawner>()));
        services.AddSingleton<RoomLayoutService>();
        services.AddSingleton<PlayerView>();
        services.AddSingleton<WorldMutationApplier>();
        services.AddSingleton<LoopWorldEditor>();
        services.AddSingleton<GameGateway>();

        // Phase 3 systems (spawners, mob AI)
        services.AddSingleton<MobSpawner>();
        services.AddSingleton<ItemSpawner>();
        services.AddSingleton<SpawnerSystem>();
        services.AddSingleton<MobAiSystem>();

        // Phase 4 systems (combat, progression)
        services.AddSingleton<CombatSystem>();

        // Phase 5 systems (abilities, quests)
        services.AddSingleton<AbilityCache>();
        services.AddSingleton<AbilitySystem>();
        services.AddSingleton<QuestCache>();

        // Game loop - wired once all systems are available
        services.AddHostedService<GameLoop>();

        return services;
    }
}
