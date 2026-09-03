using System.Text;
using System.Text.Json;

namespace Muwbta.Server.Tests.Infrastructure;

public sealed record SseFrame(long? Id, string EventType, string Data)
{
    public JsonElement Json => JsonDocument.Parse(Data).RootElement;
}

/// <summary>
/// Reads an SSE response incrementally.
/// </summary>
/// <remarks>
/// A long-lived object rather than a static helper, for two reasons that both cause
/// maddening flakiness otherwise: a StreamReader disposed between reads would close the
/// underlying HTTP stream, and even without that, its read-ahead buffer would be discarded -
/// silently dropping events that had already arrived. One reader for the life of the stream.
/// </remarks>
public sealed class SseStream(Stream stream) : IAsyncDisposable
{
    private readonly StreamReader _reader = new(
        stream,
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: false,
        bufferSize: 1024,
        leaveOpen: true);

    private readonly Stream _stream = stream;

    /// <summary>
    /// Reads frames until <paramref name="stopWhen"/> is satisfied or the timeout expires.
    /// Returns everything seen so a failing assertion can show the full transcript.
    /// </summary>
    public async Task<List<SseFrame>> ReadUntilAsync(
        Func<IReadOnlyList<SseFrame>, bool> stopWhen,
        TimeSpan timeout)
    {
        var frames = new List<SseFrame>();
        using var cts = new CancellationTokenSource(timeout);

        long? id = null;
        string? eventType = null;
        string? data = null;

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(cts.Token);
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    // A blank line terminates the current frame.
                    if (eventType is not null && data is not null)
                    {
                        frames.Add(new SseFrame(id, eventType, data));
                        if (stopWhen(frames))
                        {
                            return frames;
                        }
                    }

                    id = null;
                    eventType = null;
                    data = null;
                    continue;
                }

                if (line.StartsWith("id:", StringComparison.Ordinal))
                {
                    id = long.TryParse(line[3..].Trim(), out var parsed) ? parsed : null;
                }
                else if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    eventType = line[6..].Trim();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data = line[5..].Trim();
                }

                // Lines beginning with ':' are comments - the heartbeat - and are ignored.
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out. Return what arrived so the assertion can report it.
        }

        return frames;
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _stream.DisposeAsync();
    }
}

public static class SseFrameExtensions
{
    public static bool HasText(this IReadOnlyList<SseFrame> frames, string fragment) =>
        frames.Any(f => f.EventType == "text" && f.Data.Contains(fragment, StringComparison.Ordinal));

    /// <summary>
    /// Matches a <c>sys</c> frame - connection notices, and anything reported to a session from
    /// outside its own command flow, such as the result of an admin command (PLAN.md §7.7).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="HasText"/> on purpose. The client renders both into the same
    /// scrollback, so a matcher covering both would pass whichever channel the server happened
    /// to use - and these two are not interchangeable: <c>text</c> is game prose, <c>sys</c> is
    /// the client talking about the connection.
    /// </remarks>
    public static bool HasSys(this IReadOnlyList<SseFrame> frames, string fragment) =>
        frames.Any(f => f.EventType == "sys" && f.Data.Contains(fragment, StringComparison.Ordinal));
}
