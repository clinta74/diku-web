using System.Reflection;

namespace Muwbta.Server.Infrastructure;

/// <summary>
/// What build this is, and an endpoint that says so.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because "which version is running" had no answer.</b> The images carry
/// <c>org.opencontainers.image.revision</c> as an OCI label, which is correct and unreachable: a
/// label is metadata <i>about</i> the image, readable with <c>docker inspect</c> on the host and
/// nowhere else. Answering the question meant shelling into the NAS.
/// </para>
/// <para>
/// TrueNAS cannot help either. Third-party app catalogues were removed when Apps moved to Docker
/// in 24.10, so a custom app has no catalogue entry, no version listing, and nowhere for a
/// changelog to live. The Apps screen's update badge is the only signal it offers, and it is a
/// digest comparison rather than a version.
/// </para>
/// <para>
/// So the running process reports its own build. It is the one answer that cannot be stale, because
/// it is not a claim <i>about</i> the process - it is the process talking.
/// </para>
/// <para>
/// <b>The version is baked at publish time, not read from git.</b> There is no repository inside
/// the runtime image and there should not be. The Dockerfile takes <c>VERSION</c> and
/// <c>REVISION</c> as build arguments and passes them to <c>dotnet publish</c>, which writes them
/// into <see cref="AssemblyInformationalVersionAttribute"/> as <c>version+revision</c>. A local
/// <c>dotnet run</c> gets the defaults and says so rather than pretending to be a release.
/// </para>
/// </remarks>
public static class BuildInfo
{
    /// <summary>What a build that was never given a version calls itself.</summary>
    private const string Unknown = "unknown";

    static BuildInfo()
    {
        // InformationalVersion is `<Version>+<SourceRevisionId>` when both are set, and just
        // `<Version>` when the revision is not - so the split has to tolerate one part.
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            Version = Unknown;
            Revision = Unknown;
            return;
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        if (plus < 0)
        {
            Version = informational;
            Revision = Unknown;
            return;
        }

        Version = informational[..plus];
        var revision = informational[(plus + 1)..];
        Revision = revision.Length == 0 ? Unknown : revision;
    }

    /// <summary>The semantic version, or <c>0.0.0</c> for a build off a branch.</summary>
    public static string Version { get; }

    /// <summary>The full commit sha, or <c>unknown</c> outside a container build.</summary>
    public static string Revision { get; }

    /// <summary>The first seven characters of the revision, which is what a person reads.</summary>
    public static string ShortRevision =>
        Revision.Length >= 7 ? Revision[..7] : Revision;

    public static void MapVersionEndpoint(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Under /api/ deliberately. nginx forwards /api/ and /health and nothing else, so a
        // version route anywhere else is reachable in development and a 404 behind the client
        // image - which is exactly the environment it exists to answer questions about.
        //
        // Anonymous, like the health checks beside it. The repository is public and AGPL, so the
        // commit this names is already readable by anyone who wants it; requiring a login would
        // only stop it being useful from the sign-in screen, which is where it is shown.
        routes.MapGet("/api/version", () => Results.Ok(new VersionResponse(
            BuildInfo.Version,
            BuildInfo.Revision,
            BuildInfo.ShortRevision)))
            .WithName("GetVersion")
            .AllowAnonymous();
    }

    private sealed record VersionResponse(string Version, string Revision, string ShortRevision);
}
