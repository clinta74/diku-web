using System.Text;
using System.Text.Json;

namespace Muwbta.Playtest.Session;

/// <summary>One frame off the wire, exactly as the server wrote it.</summary>
public sealed record SseFrame(long? Id, string EventType, string Data)
{
    public JsonElement Json => JsonDocument.Parse(Data).RootElement;
}

/// <summary>
/// Reads an SSE response incrementally, yielding frames as they arrive.
/// </summary>
/// <remarks>
/// The frame-parsing loop is lifted from <c>tests/Muwbta.Server.Tests/Infrastructure/SseReader.cs</c>
/// rather than rewritten, because that version already solves two failures that are maddening to
/// diagnose: a <see cref="StreamReader"/> disposed between reads closes the underlying HTTP stream,
/// and even without that, its read-ahead buffer is discarded — silently dropping events that had
/// already arrived. One reader for the life of the stream.
///
/// What is different here is the shape. The test version reads on demand, until an assertion is
/// satisfied. An apparatus that only read while waiting would lose everything that arrived while it
/// was posting a command — which is most of the interesting output, since the server answers a
/// command asynchronously over this same stream. So this yields continuously and a background pump
/// hands every frame to the transcript, whether anyone is waiting or not.
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

    /// <summary>Every frame on this stream, until the server closes it or the token fires.</summary>
    public async IAsyncEnumerable<SseFrame> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        long? id = null;
        string? eventType = null;
        string? data = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;

            try
            {
                line = await _reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (IOException)
            {
                // The connection went away mid-frame. Ending the stream is the honest answer;
                // the recorder notes the close and the run carries on.
                yield break;
            }

            if (line is null)
            {
                yield break;
            }

            if (line.Length == 0)
            {
                // A blank line terminates the current frame.
                if (eventType is not null && data is not null)
                {
                    yield return new SseFrame(id, eventType, data);
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

            // Lines beginning with ':' are comments — the 15-second heartbeat — and are ignored.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _stream.DisposeAsync();
    }
}
