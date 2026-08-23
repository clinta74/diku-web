using System.Text.Json.Nodes;
using DikuWeb.Domain.Worlds;
using DikuWeb.Server.Building;

namespace DikuWeb.Server.Tests.Building;

/// <summary>
/// The generated schema stays in step with the registries the validator reads.
/// </summary>
/// <remarks>
/// The point of generating the schema instead of writing it down is that it cannot drift. That is
/// only true if something checks, which is what these do - the same guarantee
/// <c>ChangeRecordCompletenessTests</c> and <c>ExportScriptCompletenessTests</c> give the two other
/// places the content shape is spelled out.
/// </remarks>
public sealed class AssistSchemaTests
{
    private static readonly string[] Destinations =
    [
        "ossara.gatetown.the-market",
        "ossara.gatetown.the-north-road",
    ];

    private static JsonObject Properties(JsonObject schema) =>
        schema["properties"]!.AsObject();

    /// <summary>
    /// Every field of <see cref="BundleRoom"/> is either generated or deliberately excluded.
    /// </summary>
    /// <remarks>
    /// The test that earns the file. A field added to the record and forgotten here is a field the
    /// assist silently never fills in - which reads, from the builder's side, as the model being
    /// bad at its job rather than as nobody having decided.
    /// </remarks>
    [Fact]
    public void Every_room_field_is_generated_or_explained()
    {
        var generated = Properties(AssistSchema.ForRoom(Destinations))
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var undecided = typeof(BundleRoom)
            .GetProperties()
            .Select(p => p.Name)
            .Where(name => !generated.Contains(name) && !AssistSchema.NotGenerated.ContainsKey(name))
            .ToList();

        Assert.Empty(undecided);
    }

    /// <summary>And nothing is excluded that no longer exists.</summary>
    /// <remarks>
    /// The other direction, because a stale exclusion is a reason nobody can act on: it reads as a
    /// decision about a field, and the field is gone.
    /// </remarks>
    [Fact]
    public void Nothing_is_excluded_that_the_record_does_not_have()
    {
        var fields = typeof(BundleRoom)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stale = AssistSchema.NotGenerated.Keys.Where(k => !fields.Contains(k)).ToList();

        Assert.Empty(stale);
    }

    /// <summary>
    /// The flag vocabulary is the registry's, exactly.
    /// </summary>
    /// <remarks>
    /// This is the drift that would otherwise be invisible: registering a flag and not telling the
    /// model about it costs nothing at compile time and means the assist can never set it.
    /// </remarks>
    [Fact]
    public void The_flags_are_the_registry()
    {
        var flags = Properties(AssistSchema.ForRoom(Destinations))["flags"]!
            .AsObject()["properties"]!
            .AsObject();

        Assert.Equal(
            RoomFlags.All.Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal),
            flags.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>Each flag carries the summary the builder UI shows a person.</summary>
    [Fact]
    public void Each_flag_explains_itself()
    {
        var flags = Properties(AssistSchema.ForRoom(Destinations))["flags"]!
            .AsObject()["properties"]!
            .AsObject();

        foreach (var flag in RoomFlags.All)
        {
            Assert.Equal(
                flag.Summary,
                flags[flag.Key]!.AsObject()["description"]!.GetValue<string>());
        }
    }

    /// <summary>The directions are the engine's six, lowercased the way the bundle spells them.</summary>
    [Fact]
    public void The_directions_are_the_engines()
    {
        var direction = Properties(AssistSchema.ForRoom(Destinations))["exits"]!
            .AsObject()["items"]!
            .AsObject()["properties"]!
            .AsObject()["direction"]!
            .AsObject()["enum"]!
            .AsArray()
            .Select(v => v!.GetValue<string>());

        Assert.Equal(
            DirectionExtensions.All.Select(d => d.ToString().ToLowerInvariant()),
            direction);
    }

    /// <summary>
    /// An exit may only lead somewhere that already exists.
    /// </summary>
    /// <remarks>
    /// The one referential rule a per-entity grammar can carry, and the reason it can is that the
    /// legal set is known before generation starts. Everything else about the exit graph -
    /// reciprocity, connectivity - is a property of the whole bundle and stays with the validator.
    /// </remarks>
    [Fact]
    public void An_exit_may_only_lead_to_a_room_that_exists()
    {
        var to = Properties(AssistSchema.ForRoom(Destinations))["exits"]!
            .AsObject()["items"]!
            .AsObject()["properties"]!
            .AsObject()["to"]!
            .AsObject()["enum"]!
            .AsArray()
            .Select(v => v!.GetValue<string>());

        Assert.Equal(Destinations, to);
    }

    /// <summary>With nowhere to lead, the model is not asked for exits at all.</summary>
    /// <remarks>
    /// An empty <c>enum</c> is a rule no completion can satisfy, which is a grammar the sampler
    /// cannot get out of. Omitting the field is the honest version of "there is nothing to link to
    /// yet".
    /// </remarks>
    [Fact]
    public void No_destinations_means_no_exits_field()
    {
        var schema = AssistSchema.ForRoom([]);

        Assert.False(Properties(schema).ContainsKey("exits"));
        Assert.DoesNotContain(
            "exits",
            schema["required"]!.AsArray().Select(v => v!.GetValue<string>()));
    }

    /// <summary>
    /// The two keywords the grammar actually rests on are present at every level.
    /// </summary>
    /// <remarks>
    /// Without <c>required</c>, <c>{}</c> is a legal completion and by some distance the cheapest
    /// one. Without <c>additionalProperties: false</c>, the model may invent a field and spend the
    /// generation budget filling it in. Neither is tidiness; both change what the sampler is
    /// allowed to do.
    /// </remarks>
    [Fact]
    public void Every_object_is_closed_and_says_what_it_requires()
    {
        var schema = AssistSchema.ForRoom(Destinations);

        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            ["title", "description", "flags", "exits"],
            schema["required"]!.AsArray().Select(v => v!.GetValue<string>()));

        var item = Properties(schema)["exits"]!.AsObject()["items"]!.AsObject();

        Assert.False(item["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            ["direction", "to"],
            item["required"]!.AsArray().Select(v => v!.GetValue<string>()));

        // Flags are closed but required of nothing: an absent flag means "inherit from the zone",
        // which is a real third state and not a false.
        var flags = Properties(schema)["flags"]!.AsObject();

        Assert.False(flags["additionalProperties"]!.GetValue<bool>());
        Assert.Null(flags["required"]);
    }

    /// <summary>
    /// No constraint is expressed as a regex.
    /// </summary>
    /// <remarks>
    /// Schema-to-grammar converters support <c>pattern</c> only partially, and one that does not
    /// convert is a constraint that silently is not applied - which puts it in the same family as
    /// the truncation this whole feature had to fix first. Where the legal set is knowable it is
    /// enumerated instead, which is stricter and certain to hold.
    /// </remarks>
    [Fact]
    public void Nothing_relies_on_a_regex()
    {
        Assert.DoesNotContain(
            "\"pattern\"",
            AssistSchema.ForRoom(Destinations).ToJsonString(),
            StringComparison.Ordinal);
    }
}
