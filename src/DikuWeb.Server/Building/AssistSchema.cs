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
/// source of truth that drifts the moment somebody adds a flag - the failure
/// <c>ChangeRecordCompletenessTests</c> and <c>ExportScriptCompletenessTests</c> already exist to
/// prevent in the two other places this shape is spelled out. Reading
/// <see cref="RoomFlags.All"/> and <see cref="Direction"/> means a new flag reaches the model with
/// its own summary attached as documentation, on the commit that registers it and with no second
/// edit.
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
            ["flags"] = FlagsSchema(),
        };

        // Built through JsonValue.Create rather than the collection initialiser, and the same
        // below. JsonArray's generic Add<T> routes a bare string through the reflection-based
        // serializer, which throws outright under a host that has
        // JsonSerializerIsReflectionEnabledByDefault off - found by running this from a
        // file-based app. The server itself has reflection on and was never affected; the typed
        // overloads cost nothing and mean this cannot become a startup crash if that ever changes.
        var required = new JsonArray(
            JsonValue.Create("title"), JsonValue.Create("description"), JsonValue.Create("flags"));

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

    /// <summary>
    /// Every flag in the registry, as an optional boolean carrying its own summary.
    /// </summary>
    /// <remarks>
    /// The summaries are not decoration. They are the only thing telling the model what
    /// <c>noRecall</c> means, and they are already written, already reviewed, and already what the
    /// builder UI shows a human - so the model and the person read the same sentence.
    /// <para>
    /// Deliberately not <c>required</c>: a flag absent from a room means "inherit", which is a
    /// different thing from false (PLAN.md §4.10, and <see cref="RoomFlags.Resolve"/>). Forcing
    /// all eight would make the model assert a decision about every flag on every room, and the
    /// assertion would usually be wrong.
    /// </para>
    /// </remarks>
    private static JsonObject FlagsSchema()
    {
        var flags = new JsonObject();

        foreach (var flag in RoomFlags.All)
        {
            flags[flag.Key] = new JsonObject
            {
                ["type"] = "boolean",
                ["description"] = flag.Summary,
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = flags,
            ["additionalProperties"] = false,
            ["description"] = "Only flags this room actually decides. Omit a flag to inherit it "
                + "from the zone.",
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
