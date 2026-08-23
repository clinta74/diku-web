using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DikuWeb.Server.Building;
using Microsoft.Extensions.Options;

namespace DikuWeb.Server.Assist;

/// <summary>
/// Drafts content by asking a local Ollama, with the output shape constrained by
/// <see cref="AssistSchema"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ollama's native API, not its OpenAI-compatible one.</b> The compatible surface has no field
/// for <c>num_ctx</c>, and <c>keep_alive</c> is likewise only reachable here. Given that everything
/// about this design is arranged around one large prefix staying cached, giving up the two
/// parameters that control the window and the residency to gain a familiar request shape would be
/// a poor trade.
/// </para>
/// <para>
/// <b>The prompt is built in one order, always.</b> Canon, then zone, then the room, then whatever
/// the builder typed. Everything that varies goes after everything that does not, because the KV
/// cache is reused for exactly as long as the prefix matches - so a builder's free-text steer, the
/// most variable thing in the request, is last by construction rather than by care.
/// </para>
/// </remarks>
public sealed class OllamaContentAssistant : IContentAssistant
{
    /// <summary>Low, because this output has to satisfy a schema and be believed.</summary>
    private const double Temperature = 0.4;

    /// <summary>How many of the zone's rooms are shown as exemplars.</summary>
    /// <remarks>
    /// Three, and the budget is why: the canon is 10,183 tokens of a 16,384 window, the schema is
    /// another 946, and a room description runs to 900 characters. Three exemplars is roughly 700
    /// tokens and leaves room to generate. This is the number to revisit if the window changes,
    /// and <c>CanonTests</c> is what will notice if the canon grows into it first.
    /// </remarks>
    private const int ExemplarCount = 3;

    private readonly HttpClient _http;
    private readonly AssistOptions _options;
    private readonly ILogger<OllamaContentAssistant> _logger;

    public OllamaContentAssistant(
        HttpClient http,
        IOptions<AssistOptions> options,
        ILogger<OllamaContentAssistant> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RoomDraft> DraftRoomAsync(
        RoomDraftRequest request,
        ZoneContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // The room being drafted is not somewhere it can link to. Left in, a model that has been
        // told the room exists will happily give it an exit to itself.
        var destinations = context.RoomKeys
            .Where(k => !string.Equals(k, request.RoomKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var payload = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["format"] = AssistSchema.ForRoom(destinations),
            ["prompt"] = Prompt(request, context),
            ["options"] = new JsonObject { ["temperature"] = Temperature },
            // Never let one request be the reason the model unloads: an unload discards the canon
            // prefix, and rebuilding it is minutes rather than seconds.
            ["keep_alive"] = -1,
        };

        using var content = new StringContent(
            payload.ToJsonString(), Encoding.UTF8, "application/json");

        AssistLog.Requesting(_logger, _options.Model, request.RoomKey);

        using var response = await _http
            .PostAsync(new Uri("/api/generate", UriKind.Relative), content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException(
                $"The model refused the request ({(int)response.StatusCode}). {Trim(body)}");
        }

        var envelope = await response.Content
            .ReadFromJsonAsync(AssistJsonContext.Default.GenerateResponse, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The model returned nothing at all.");

        AssistLog.Generated(
            _logger, request.RoomKey, envelope.PromptEvalCount, envelope.EvalCount);

        return Parse(envelope.Response);
    }

    /// <summary>
    /// Canon first, unchanged, every time.
    /// </summary>
    /// <remarks>
    /// The first line after the canon starts a section the model can tell apart from the world
    /// itself, because the canon is a document about a world and what follows is an instruction
    /// about a room - and without a visible seam the second reads as more of the first.
    /// </remarks>
    private static string Prompt(RoomDraftRequest request, ZoneContext context)
    {
        var prompt = new StringBuilder(Canon.Prefix);

        prompt.Append("\n---\n\nYou are drafting one room for a builder, in the world above.\n\n")
            .Append("Zone: ").Append(context.ZoneName).Append('\n')
            .Append(context.ZoneDescription).Append("\n\n");

        if (context.Exemplars.Count > 0)
        {
            prompt.Append("Rooms already written in this zone, for voice and length:\n\n");

            foreach (var exemplar in context.Exemplars.Take(ExemplarCount))
            {
                prompt.Append("## ").Append(exemplar.Title).Append('\n')
                    .Append(exemplar.Description).Append("\n\n");
            }
        }

        prompt.Append("Write the room `").Append(request.RoomKey).Append("`.\n")
            .Append("Describe only what is in the room. Do not name mobs, items, or exits: the ")
            .Append("engine lists those, and anything you invent here will not exist.\n");

        // Last, always. Anything a builder types must not be able to move the cached prefix.
        if (!string.IsNullOrWhiteSpace(request.Instruction))
        {
            prompt.Append("\nThe builder adds: ").Append(request.Instruction.Trim()).Append('\n');
        }

        return prompt.ToString();
    }

    private static RoomDraft Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The model returned an empty response.");
        }

        RoomDraft? draft;

        try
        {
            draft = JsonSerializer.Deserialize(json, AssistJsonContext.Default.RoomDraft);
        }
        catch (JsonException e)
        {
            // Should be impossible - the grammar guarantees this parses - so if it happens the
            // interesting fact is that the constraint did not hold, and the body is the evidence.
            throw new InvalidOperationException(
                $"Constrained output did not parse, which should not be possible: {Trim(json)}", e);
        }

        if (draft is null || string.IsNullOrWhiteSpace(draft.Title) ||
            string.IsNullOrWhiteSpace(draft.Description))
        {
            throw new InvalidOperationException($"The draft has no title or no prose: {Trim(json)}");
        }

        return draft;
    }

    private static string Trim(string? text) =>
        text is null ? string.Empty
        : text.Length <= 400 ? text
        : text[..400] + "...";

}

/// <summary>The fields of Ollama's reply this cares about.</summary>
/// <remarks>
/// <b>The names are spelled out because Ollama speaks snake_case and this codebase does not.</b>
/// Under <c>JsonSerializerDefaults.Web</c> these bound to <c>promptEvalCount</c>, matched nothing,
/// and silently deserialised as zero - so the log line built to be the truncation canary would have
/// reported "0 prompt tokens" forever, which is worse than not logging it. The stub in the tests
/// did not catch it because a test that does not assert a number cannot notice it is zero.
/// </remarks>
internal sealed record GenerateResponse(
    [property: JsonPropertyName("response")] string? Response,
    [property: JsonPropertyName("prompt_eval_count")] int PromptEvalCount,
    [property: JsonPropertyName("eval_count")] int EvalCount);

/// <summary>
/// Source-generated metadata for the two shapes this crosses the wire with.
/// </summary>
/// <remarks>
/// <b>Not decoration, and not premature.</b> Reflection-based serialisation is disabled in some
/// hosts - a file-based app under <c>tools/</c> is one, which is how this was found - and it throws
/// outright rather than degrading. The server enables reflection and was never affected, but this
/// is the second time the same trap has been hit in this feature (the first was
/// <c>JsonArray.Add&lt;T&gt;</c> in <c>AssistSchema</c>), and it costs ten lines to stop being a
/// trap: any tool that wants to draft a room from a script now can.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GenerateResponse))]
[JsonSerializable(typeof(RoomDraft))]
internal sealed partial class AssistJsonContext : JsonSerializerContext;
