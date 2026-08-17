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
    /// <para>
    /// <b>Seeded from <see cref="JsonSerializerDefaults.Web"/>, which is where a minimal API
    /// starts.</b> Not camelCase alone: Web also sets <c>PropertyNameCaseInsensitive</c>, and that
    /// one is load-bearing here. The authored content carries <c>MobAttack</c> in PascalCase, so a
    /// case-sensitive reader binds <em>none</em> of its four properties and every attack in the
    /// world comes back as the record's defaults — a generic "hit" every eight pulses, with any
    /// effect on it gone. It reads clean, throws nothing, and is wrong.
    /// </para>
    /// <para>
    /// That is not a hypothetical either: this type was written with a bare camelCase options
    /// object and did exactly that, silently, until a merge round-tripped a mob and the clamp arm
    /// came back swinging a fist. The endpoint was always fine, which is the point — a reader that
    /// is <em>nearly</em> the endpoint's is worse than an obviously different one.
    /// </para>
    /// </remarks>
    public static JsonSerializerOptions SerializerOptions { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

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

    /// <summary>
    /// Writes a bundle the way an export does: same converters, indented to stay readable.
    /// </summary>
    /// <remarks>
    /// Here rather than at the call site so a tool never touches <see cref="JsonSerializer"/>
    /// directly. That is not only symmetry with <see cref="TryRead"/>: a file-based app
    /// (<c>dotnet run tools/*.cs</c>) turns on trim analysis, and this solution treats warnings as
    /// errors, so a bare <c>Serialize</c> call in a shim fails the build with IL2026 and IL3050.
    /// Inside this project — which is neither trimmed nor AOT-published — it compiles clean, and
    /// the shim stays thin, which it should be anyway.
    /// </remarks>
    public static string Write(WorldBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        return JsonSerializer.Serialize(
            bundle,
            new JsonSerializerOptions(SerializerOptions) { WriteIndented = true });
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
