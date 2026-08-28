using DikuWeb.Playtest.Session;

namespace DikuWeb.Playtest.Load;

/// <summary>
/// Reads <c>/metrics</c> off the server under test.
/// </summary>
/// <remarks>
/// <para>
/// <b>The verdict comes from here, not from the generator's own timings.</b> A command POST returns
/// <c>202 Accepted</c> the moment it is queued — <c>GameEndpoints.SubmitCommand</c> hands it to the
/// gateway and returns without waiting for the loop — so anything this apparatus measures at the
/// socket is the time to enqueue a string. Under a loop that had fallen seconds behind, that number
/// would stay beautiful. Pulse duration is the thing that decides whether the server is keeping up,
/// it lives inside the process, and this endpoint is how it gets out.
/// </para>
/// <para>
/// Unauthenticated by design and expected to be unreachable from outside the deployment, so no
/// credential is needed and none should be added: a load run points at a box it already controls.
/// </para>
/// </remarks>
public sealed class MetricsProbe(Uri baseAddress) : IDisposable
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = baseAddress,
        Timeout = TimeSpan.FromSeconds(20),
    };

    public async Task<MetricsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var response = await _client.GetAsync(
            new Uri("/metrics", UriKind.Relative), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new PlaytestException(
                $"/metrics answered {(int)response.StatusCode}. A load run cannot report anything "
                + "without it — check the exporter is enabled on the server under test.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var snapshot = MetricsSnapshot.Read(body, DateTimeOffset.UtcNow);

        if (snapshot.Pulse.Count == 0)
        {
            throw new PlaytestException(
                $"/metrics carried no '{MetricsSnapshot.PulseFamily}' samples. Either the game "
                + "loop is not running in this process, or the instrument was renamed and this "
                + "apparatus is looking for the wrong name.");
        }

        return snapshot;
    }

    public void Dispose() => _client.Dispose();
}
