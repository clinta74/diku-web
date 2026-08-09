using DikuWeb.Engine.Systems;

namespace DikuWeb.Server.Infrastructure;

/// <summary>
/// Lets the game loop stop the process it is hosted in (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// The Engine does not know it is hosted, so it asks through
/// <see cref="IShutdownSignal"/> and this is the Server's answer — the same shape as
/// <c>IWorldSource</c> and the repository adapters, and for the same reason.
///
/// <see cref="IHostApplicationLifetime.StopApplication"/> rather than anything more direct,
/// because everything that makes a shutdown safe is already wired to it: the loop's stopping
/// token is cancelled, the loop saves every player on its way out, and the save workers drain
/// before the process ends. Killing the process here would skip all three.
/// </remarks>
internal sealed class HostShutdownSignal(IHostApplicationLifetime lifetime) : IShutdownSignal
{
    public void Stop() => lifetime.StopApplication();
}
