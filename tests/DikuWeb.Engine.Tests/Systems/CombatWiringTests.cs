using DikuWeb.Engine;
using DikuWeb.Engine.Commands;
using DikuWeb.Engine.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Combat's template caches and the command table's clock arrive as optional constructor
/// parameters, which means a container that stops supplying them fails silently: every weapon
/// reverts to the default speed, every mob narrates "hit", and every cast becomes instant. None
/// of that throws, and none of the behaviour tests would notice, because they construct their
/// collaborators by hand. So the wiring itself is the assertion here.
/// </summary>
public sealed class CombatWiringTests
{
    [Fact]
    public void The_container_gives_combat_its_template_caches()
    {
        using var provider = BuildEngine();

        Assert.True(provider.GetRequiredService<CombatSystem>().HasTemplateCaches);
    }

    [Fact]
    public void The_container_gives_the_command_table_a_clock()
    {
        // Without it a cast time is measured from pulse zero, so every spell resolves instantly
        // and "your swings pause while you cast" can never be observed.
        using var provider = BuildEngine();

        Assert.NotNull(provider.GetRequiredService<CommandRegistry>());
        Assert.NotNull(provider.GetRequiredService<DikuWeb.Engine.Time.IGameClock>());
    }

    private static ServiceProvider BuildEngine()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDikuWebEngine();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
