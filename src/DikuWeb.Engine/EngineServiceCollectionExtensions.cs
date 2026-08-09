using DikuWeb.Domain.Abilities.Effects;
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
using Microsoft.Extensions.Logging;

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
                sp.GetService<IMobTemplateRepository>(),
                sp.GetService<IItemTemplateRepository>(),
                sp.GetService<MobSpawner>(),
                sp.GetService<ItemSpawner>(),
                sp.GetService<IGameClock>(),
                sp.GetService<EffectRegistry>()));
        services.AddSingleton<RoomLayoutService>();
        services.AddSingleton<PlayerView>();
        services.AddSingleton<WorldMutationApplier>();
        services.AddSingleton<LoopWorldEditor>();
        services.AddSingleton<GameGateway>();

        // Phase 3 systems (spawners, mob AI). Both take the caches so their sweeps read memory
        // instead of querying every tick - per-key template lookups for both, plus the whole
        // spawners table for the sweep; both fall back to the repository if a cache was never
        // loaded, so a container that omitted one costs throughput rather than behaviour.
        services.AddSingleton<MobSpawner>();
        services.AddSingleton<ItemSpawner>();
        services.AddSingleton<SpawnerSystem>(sp =>
            new SpawnerSystem(
                sp.GetRequiredService<ISpawnerRepository>(),
                sp.GetRequiredService<IMobTemplateRepository>(),
                sp.GetRequiredService<IItemTemplateRepository>(),
                sp.GetRequiredService<MobSpawner>(),
                sp.GetRequiredService<ItemSpawner>(),
                sp.GetRequiredService<ILogger<SpawnerSystem>>(),
                sp.GetService<PlayerView>(),
                sp.GetService<MobTemplateCache>(),
                sp.GetService<ItemTemplateCache>(),
                sp.GetService<SpawnerCache>()));
        services.AddSingleton<MobAiSystem>(sp =>
            new MobAiSystem(
                sp.GetRequiredService<IMobTemplateRepository>(),
                sp.GetRequiredService<IRandomSource>(),
                sp.GetRequiredService<IGameClock>(),
                sp.GetRequiredService<PlayerView>(),
                sp.GetService<MobTemplateCache>()));

        // Phase 4 systems (combat, progression). Constructed explicitly rather than by
        // convention: the template caches are optional parameters, and a container that quietly
        // failed to supply them would leave every weapon at the default speed and every mob
        // narrating "hit" - a balance-shaped bug with no exception to notice.
        services.AddSingleton<CombatSystem>(sp =>
            new CombatSystem(
                sp.GetRequiredService<EngineOptions>(),
                sp.GetService<PlayerView>(),
                sp.GetService<ItemTemplateCache>(),
                sp.GetService<MobTemplateCache>(),
                sp.GetService<ItemSpawner>(),
                sp.GetService<ILogger<CombatSystem>>(),
                // Without this a mob attack carrying an effect swings for its damage and applies
                // nothing - the silent half-feature this codebase keeps having to relearn.
                sp.GetService<EffectRegistry>()));

        // Phase 5 systems (abilities, quests). Explicit for the same reason as CombatSystem: the
        // mob templates are what tell an area effect which mobs are non-combatants, and a
        // container that quietly failed to supply them would let a stray Firestorm kill the
        // shopkeeper standing behind the wolves.
        services.AddSingleton<AbilityCache>();
        services.AddSingleton<AbilitySystem>(sp =>
            new AbilitySystem(
                sp.GetRequiredService<IGameClock>(),
                sp.GetService<AbilityCache>(),
                sp.GetService<EffectRegistry>(),
                sp.GetService<ILogger<AbilitySystem>>(),
                sp.GetService<MobTemplateCache>()));
        services.AddSingleton<QuestCache>();
        services.AddSingleton<ItemTemplateCache>();
        services.AddSingleton<MobTemplateCache>();
        services.AddSingleton<SpawnerCache>();

        // Game loop - wired once all systems are available
        services.AddHostedService<GameLoop>();

        return services;
    }
}
