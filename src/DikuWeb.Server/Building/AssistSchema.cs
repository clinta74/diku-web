using System.Text.Json.Nodes;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Server.Building;

/// <summary>
/// The JSON Schema the builder assist constrains generation to, built from the registries the
/// validator itself reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a grammar can and cannot do, because the difference decides the whole design.</b>
/// Constrained decoding guarantees the output <em>parses</em> and is <em>typed</em>: the keys are
/// the keys, the enums are in range, nothing trails off mid-token. It cannot guarantee the output
/// is <em>correct</em>. <see cref="BundleValidator"/> runs twelve checks and a grammar can carry
/// perhaps three of them; reciprocity, connectivity, spawner targets and quest reachability are
/// properties of a whole graph, and no per-entity schema sees the graph.
/// </para>
/// <para>
/// <b>So the grammar is not a validator, and the sharp edge is that it cannot fail.</b> There is no
/// such thing as a refused generation - the sampler simply has fewer legal tokens, and something
/// schema-shaped always comes out. A model with nothing useful to say emits a plausible room with
/// a plausible exit rather than visible nonsense. That trades unparseable garbage for well-formed
/// wrong, which is <em>harder</em> to catch by eye, and it is the same failure class as a
/// truncated canon prefix (tools/ollama/README.md). Everything generated here goes through
/// <see cref="BundleValidator"/> before a builder is shown it - not on the way to Save, which is
/// too late to be a review.
/// </para>
/// <para>
/// <b>Generated rather than written down.</b> A hand-authored copy of the room shape is a second
/// source of truth that drifts the moment somebody changes the first - the failure
/// <c>ChangeRecordCompletenessTests</c> and <c>ExportScriptCompletenessTests</c> already exist to
/// prevent in the two other places this shape is spelled out. Reading
/// <see cref="Direction"/> means the vocabulary is the engine's by construction, and
/// <see cref="NotGenerated"/> plus its test means a field added to <see cref="BundleRoom"/> has to
/// be decided about rather than forgotten.
/// </para>
/// <para>
/// <b>What it is <em>for</em> is narrower than what it could carry, and that was learned rather
/// than designed.</b> Room flags were generated here first - eight booleans straight off the
/// registry, each documented by the summary the builder UI already shows a person, which read as
/// the best argument in the file. The first real generation set <c>respawn: true</c>, making the
/// room a bind point, against a world rule (WORLD.md 4.1) that no validator enforces and that
/// another rule leans on for safety. The model was not malfunctioning; it was filling in a field
/// it had been handed. The lesson is in <see cref="NotGenerated"/>: ask it for prose, and do not
/// hand it the mechanical fields just because they are easy to describe.
/// </para>
/// <para>
/// <b>Constraints are expressed as <c>enum</c>, never as <c>pattern</c>.</b> The schema-to-grammar
/// converters support regex only partially, and a pattern that silently does not convert is a
/// constraint that silently is not applied. Where the legal set is knowable - directions, flags,
/// the rooms an exit may lead to - it is enumerated, which is both stricter and certain to hold.
/// </para>
/// </remarks>
public static class AssistSchema
{
    /// <summary>Room titles are a line, not a paragraph.</summary>
    public const int TitleMaxLength = 60;

    /// <summary>Long enough for a room, short enough that it cannot run away on a CPU.</summary>
    public const int DescriptionMaxLength = 900;

    /// <summary>
    /// The fields of <see cref="BundleRoom"/> the model is <em>not</em> asked for, and why.
    /// </summary>
    /// <remarks>
    /// Held as data rather than left implicit so that <c>AssistSchemaTests</c> can insist every
    /// field of the record is either generated or deliberately excluded. A field added to
    /// <see cref="BundleRoom"/> and forgotten here fails that test rather than quietly becoming a
    /// thing the assist never fills in.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> NotGenerated =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Key"] =
                "The builder decides where a room lives. A generated key either collides with an "
                + "existing room or dangles, and the server already knows the right one.",
            ["ZoneKey"] =
                "Follows from Key, and CheckRooms errors when the two disagree.",
            ["Grid"] =
                "The layout editor owns the map. Terrain is spatial, not prose.",
            ["Legend"] =
                "Half of Grid; meaningless without it.",
            ["EditorX"] =
                "Where the room sits on the builder's canvas, which is not a property of the room.",
            ["EditorY"] =
                "As EditorX.",
            ["Flags"] =
                "Withdrawn after the first real generation set respawn:true on a room. WORLD.md "
                + "4.1 is emphatic - respawn is five zones, the hubs, and nowhere else - and it "
                + "resolves at zone level, so a room setting it is wrong twice over. Nothing "
                + "catches it: BundleValidator has no rule about bind points, and 4.1 leans on "
                + "that policy to make the conditional-exit exploit impossible by construction. "
                + "Flags are mechanical and canon-governed; prose is what the model is for.",
        };

    /// <summary>
    /// The schema for one room's authored content.
    /// </summary>
    /// <param name="exitDestinations">
    /// The room keys an exit may lead to - normally the zone's existing rooms. Enumerating them is
    /// what stops the model inventing a destination: it is the one referential rule a per-entity
    /// grammar <em>can</em> carry, because the legal set is known before generation starts. Pass
    /// empty to omit exits entirely, which is the right call when the caller means to draw them
    /// itself.
    /// </param>
    public static JsonObject ForRoom(IReadOnlyCollection<string> exitDestinations)
    {
        ArgumentNullException.ThrowIfNull(exitDestinations);

        var properties = new JsonObject
        {
            ["title"] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = TitleMaxLength,
                ["description"] = "The room's name as it appears on the look line. A noun phrase, "
                    + "no leading article, no trailing punctuation.",
            },
            ["description"] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = DescriptionMaxLength,
                ["description"] = "What a character sees standing here. Present tense, second "
                    + "person implied, no mention of exits - the engine lists those itself.",
            },
        };

        // Built through JsonValue.Create rather than the collection initialiser, and the same
        // below. JsonArray's generic Add<T> routes a bare string through the reflection-based
        // serializer, which throws outright under a host that has
        // JsonSerializerIsReflectionEnabledByDefault off - found by running this from a
        // file-based app. The server itself has reflection on and was never affected; the typed
        // overloads cost nothing and mean this cannot become a startup crash if that ever changes.
        var required = new JsonArray(
            JsonValue.Create("title"), JsonValue.Create("description"));

        if (exitDestinations.Count > 0)
        {
            properties["exits"] = ExitsSchema(exitDestinations);
            required.Add(JsonValue.Create("exits"));
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            // Both are load-bearing for a grammar rather than merely tidy. Without `required` the
            // cheapest legal completion is `{}`; without this, the model may invent a field and
            // spend its budget filling one in.
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }


    private static JsonObject ExitsSchema(IReadOnlyCollection<string> destinations)
    {
        var directions = new JsonArray();

        foreach (var direction in DirectionExtensions.All)
        {
            directions.Add(JsonValue.Create(direction.ToString().ToLowerInvariant()));
        }

        var to = new JsonArray();

        foreach (var key in destinations)
        {
            to.Add(JsonValue.Create(key));
        }

        return new JsonObject
        {
            ["type"] = "array",
            ["maxItems"] = DirectionExtensions.All.Count,
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["direction"] = new JsonObject
                    {
                        ["enum"] = directions,
                        ["description"] = "Which way out. Each direction at most once.",
                    },
                    ["to"] = new JsonObject
                    {
                        ["enum"] = to,
                        ["description"] = "An existing room. Only these may be linked to.",
                    },
                },
                ["required"] = new JsonArray(
                    JsonValue.Create("direction"), JsonValue.Create("to")),
                ["additionalProperties"] = false,
            },
            // Gating - requiredFlagKey, requiredItemKey, refusalMessage - is absent on purpose.
            // Whether a door needs a key is a design decision with consequences the model cannot
            // see: CheckQuests asks whether the required item is obtainable at all, and that is a
            // question about the whole world.
            ["description"] = "Exits from this room. The engine writes the exit line; do not "
                + "describe them in the prose.",
        };
    }
}
