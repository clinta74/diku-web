using System.Net;
using System.Text.Json;
using Muwbta.Server.Infrastructure;
using Muwbta.Server.Tests.Infrastructure;

namespace Muwbta.Server.Tests;

/// <summary>
/// The server reporting its own build.
/// </summary>
/// <remarks>
/// <para>
/// The images carry <c>org.opencontainers.image.revision</c>, which is correct and unreachable: a
/// label describes the image and is read with <c>docker inspect</c> on the host. TrueNAS offers
/// nothing either — third-party app catalogues were removed when Apps moved to Docker in 24.10, so
/// a custom app has no version listing and nowhere for a changelog to live.
/// </para>
/// <para>
/// Two properties are load-bearing and both are asserted here: it is under <c>/api/</c>, because
/// nginx forwards <c>/api/</c> and <c>/health</c> and nothing else, so a route anywhere else works
/// in development and 404s behind the client image — the exact environment it exists to answer
/// questions about. And it is anonymous, because it is shown on the sign-in screen.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class VersionEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_version_is_readable_without_signing_in()
    {
        using var client = postgres.App.CreateClient();

        var response = await client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        // Whatever the test host was built as - the point is that all three are present and
        // non-empty, not what they say. Asserting a number here would pin the test to the build.
        foreach (var field in new[] { "version", "revision", "shortRevision" })
        {
            Assert.True(
                body.TryGetProperty(field, out var value),
                $"/api/version did not report {field}");

            Assert.False(
                string.IsNullOrWhiteSpace(value.GetString()),
                $"/api/version reported an empty {field}");
        }
    }

    [Fact]
    public async Task The_short_revision_is_a_prefix_of_the_revision()
    {
        // What the badge shows has to be the front of what the tooltip shows, or somebody
        // comparing the UI against `git log` is comparing two different things.
        using var client = postgres.App.CreateClient();

        var body = JsonDocument
            .Parse(await (await client.GetAsync("/api/version")).Content.ReadAsStringAsync())
            .RootElement;

        var revision = body.GetProperty("revision").GetString()!;
        var shortRevision = body.GetProperty("shortRevision").GetString()!;

        Assert.StartsWith(shortRevision, revision, StringComparison.Ordinal);
    }
}

/// <summary>
/// Reading the version out of the assembly, with no host in the picture.
/// </summary>
public sealed class BuildInfoTests
{
    [Fact]
    public void A_build_with_no_version_says_so_rather_than_guessing()
    {
        // Whatever this assembly was built as, the two must never come back empty: the UI decides
        // between "v1.0.0" and a commit by comparing against these, and an empty string would
        // render as a blank badge that looks like a rendering bug rather than a missing version.
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Version));
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Revision));
    }

    [Fact]
    public void The_short_revision_never_exceeds_the_revision()
    {
        // `unknown` is shorter than seven characters, so the slice has to tolerate a short value
        // rather than assuming a sha is always there.
        Assert.True(BuildInfo.ShortRevision.Length <= BuildInfo.Revision.Length);
        Assert.StartsWith(BuildInfo.ShortRevision, BuildInfo.Revision, StringComparison.Ordinal);
    }
}
