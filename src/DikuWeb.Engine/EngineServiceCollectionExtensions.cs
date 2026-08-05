using DikuWeb.Engine.Commands;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;
using Microsoft.Extensions.DependencyInjection;

namespace DikuWeb.Engine;

public static class EngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the game loop and everything it owns. The host must also register:
    /// - <see cref="IWorldSource"/> for loading world data
    /// - <see cref="ICharacterSaveQueue"/> for character persistence
    /// - <see cref="IMobTemplateRepository"/> for mob templates
    /// - <see cref="IItemTemplateRepository"/> for item templates
    /// - <see cref="ISpawnerRepository"/> for spawner configuration
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

        // Singletons because the world is a single shared object owned by one thread.
        services.AddSingleton<WorldState>();
        services.AddSingleton<CommandRegistry>();
        services.AddSingleton<RoomLayoutService>();
        services.AddSingleton<PlayerView>();
        services.AddSingleton<WorldMutationApplier>();
        services.AddSingleton<LoopWorldEditor>();
        services.AddSingleton<GameGateway>();
        services.AddSingleton<SpawnerSystem>();
        services.AddSingleton<MobSpawner>();
        services.AddSingleton<ItemSpawner>();

        services.AddHostedService<GameLoop>();

        return services;
    }
}
