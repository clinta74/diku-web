using DikuWeb.Engine.Commands;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;
using Microsoft.Extensions.DependencyInjection;

namespace DikuWeb.Engine;

public static class EngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the game loop and everything it owns. The host must also register an
    /// <see cref="IWorldSource"/> and an <see cref="ICharacterSaveQueue"/>, which live in the
    /// Server because the Engine does not reference the persistence layer.
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

        services.AddHostedService<GameLoop>();

        return services;
    }
}
