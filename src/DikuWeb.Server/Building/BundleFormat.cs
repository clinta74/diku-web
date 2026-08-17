using System.Text.Json;
using System.Text.Json.Serialization;
using DikuWeb.Persistence.Converters;

namespace DikuWeb.Server.Building;

/// <summary>
/// How a <see cref="WorldBundle"/> is read, whoever is reading it (PLAN.md §6).
/// </summary>
/// <remarks>
/// <para>
/// <b>One definition, because a bundle now arrives by two doors.</b> The import endpoint gets one
/// through model binding, configured by <c>ConfigureHttpJsonOptions</c> in <c>Program.cs</c>;
/// tooling reads one off disk with no server anywhere. Those two agreeing was previously a matter
/// of nobody noticing they had drifted, and the drift is silent in the worst direction: a checker
/// that parses a bundle the endpoint would refuse reports OK on a file that cannot be imported.
/// </para>
/// <para>
/// <b>The converters are load-bearing and were the thing that proved it.</b> A plain
/// <c>JsonSerializerOptions</c> with camelCase naming and nothing else throws on the first
/// path-restricted item in the Reaches — <c>"paths": ["Warden"]</c> cannot bind to
/// <see cref="Domain.Characters.CharacterPath"/> without <see cref="JsonStringEnumConverter"/> —
/// so a tool rolling its own is not reading the same format the server accepts. It is not a
/// hypothetical: it is what the first attempt at reading a bundle outside the server did.
/// </para>
/// <para>
/// <b><see cref="TemplateKindConverter"/> comes first, deliberately.</b> Converters are consulted in
/// order and it is the stricter of the two: it refuses a number where the general converter would
/// take one. Property-level <c>[JsonConverter]</c> attributes still beat both, which is what keeps
/// <c>NullableEnumConverter</c> and its tolerance for an unrecognised value on the request records.
/// </para>
/// <para>
/// The version itself stays on <see cref="WorldBundle.CurrentFormatVersion"/>, beside the changelog
/// explaining every bump. This exposes it rather than restating it — a second copy of the number
/// would be the exact failure this type exists to close.
/// </para>
/// </remarks>
public static class BundleFormat
{
    /// <summary>The only format this build writes, and the only one it reads.</summary>
    public const int CurrentVersion = WorldBundle.CurrentFormatVersion;

    /// <summary>
    /// The converters every bundle needs, in the order they must be consulted.
    /// </summary>
    /// <remarks>
    /// Handed to <c>ConfigureHttpJsonOptions</c> as well, so the request pipeline and
    /// <see cref="SerializerOptions"/> cannot disagree about what a bundle looks like. New
    /// instances per call because <c>JsonSerializerOptions</c> takes ownership of what it is given
    /// and freezes it on first use.
    /// </remarks>
    public static IEnumerable<JsonConverter> Converters() =>
    [
        new TemplateKindConverter(),
        new JsonStringEnumConverter(),
    ];

    /// <summary>
    /// Reading a bundle outside the request pipeline: tooling, tests, anything with a file path.
    /// </summary>
    /// <remarks>
    /// camelCase to match what the pipeline applies by default, plus the same converters. Built
    /// once and reused, which also freezes it — so a caller cannot quietly add a converter here and
    /// leave the endpoint reading something else.
    /// </remarks>
    public static JsonSerializerOptions SerializerOptions { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        foreach (var converter in Converters())
        {
            options.Converters.Add(converter);
        }

        return options;
    }

    /// <summary>
    /// Parses a bundle exactly as the import endpoint would, or explains why it could not.
    /// </summary>
    /// <remarks>
    /// Deliberately does <b>not</b> check the version. Reading the file and judging what it claims
    /// to be are two questions, and a caller wants to report them separately: "this is not a bundle"
    /// and "this is a bundle for a different build" are different problems with different fixes.
    /// <see cref="IsCurrent"/> is the second question.
    /// </remarks>
    public static bool TryRead(string json, out WorldBundle? bundle, out string? error)
    {
        try
        {
            bundle = JsonSerializer.Deserialize<WorldBundle>(json, SerializerOptions);
            error = bundle is null ? "the document is null" : null;
            return bundle is not null;
        }
        catch (JsonException failure)
        {
            // The message carries the JSON path and line, which is the whole value of reporting
            // this from the real record rather than from a hand-rolled shape check.
            bundle = null;
            error = failure.Message;
            return false;
        }
    }

    /// <summary>Whether this build can import that bundle at all.</summary>
    public static bool IsCurrent(WorldBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return bundle.FormatVersion == CurrentVersion;
    }

    /// <summary>
    /// The refusal, worded once. The import endpoint's only hard refusal (§7.4), and now also what
    /// tooling says, so a builder meets the same sentence wherever they hit it.
    /// </summary>
    public static string VersionRefusal(int formatVersion) =>
        $"This is a version {formatVersion} bundle; this server reads version {CurrentVersion}.";
}
