using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DikuWeb.Server.Game;
using DikuWeb.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DikuWeb.Server.Tests;

/// <summary>
/// The drawn realm maps (<c>content/map/*.svg</c>), served to any player who is logged in.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class MapEndpointTests(PostgresFixture postgres)
{
    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private sealed record Sheet(string World, string Title, int Width, int Height);

    [Fact]
    public async Task The_maps_are_closed_to_somebody_who_is_not_logged_in()
    {
        using var client = NewClient(postgres.App);

        var list = await client.GetAsync(new Uri("/api/maps", UriKind.Relative));
        var sheet = await client.GetAsync(new Uri("/api/maps/ossara", UriKind.Relative));

        // 401 rather than 403: there is no role to fail, only a session that is not there.
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sheet.StatusCode);
    }

    [Fact]
    public async Task An_ordinary_player_may_read_the_maps()
    {
        // The whole point of the feature, and the one thing that separates it from everything
        // else drawn from content: no Builder role, no character id, no attunement.
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterAsync(client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        Assert.NotNull(sheets);
        Assert.NotEmpty(sheets);
    }

    [Fact]
    public async Task Every_realm_this_build_carries_is_listed_with_a_title_and_a_size()
    {
        // Not a count of realms - that is content, and counting it here would make authoring a
        // sixth realm a failing test. What must hold is that each sheet describes itself, since
        // the client sizes its frame from these before the image arrives.
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterAsync(client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        Assert.NotNull(sheets);

        foreach (var sheet in sheets)
        {
            Assert.False(string.IsNullOrWhiteSpace(sheet.World));
            Assert.False(string.IsNullOrWhiteSpace(sheet.Title));
            Assert.True(sheet.Width > 0, $"'{sheet.World}' reported width {sheet.Width}.");
            Assert.True(sheet.Height > 0, $"'{sheet.World}' reported height {sheet.Height}.");
        }
    }

    [Fact]
    public async Task A_sheet_comes_back_as_an_svg()
    {
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterAsync(client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        var world = sheets![0].World;
        var response = await client.GetAsync(new Uri($"/api/maps/{world}", UriKind.Relative));

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<svg", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sheet_the_client_already_has_is_not_sent_again()
    {
        // These are the largest things the game serves and they change only on a deploy. A client
        // that reopens the map should be answered in a few hundred bytes, which is the whole
        // reason the response carries a content-addressed ETag.
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterAsync(client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        var world = sheets![0].World;
        var first = await client.GetAsync(new Uri($"/api/maps/{world}", UriKind.Relative));
        var etag = first.Headers.ETag;

        Assert.NotNull(etag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/maps/{world}", UriKind.Relative));
        conditional.Headers.IfNoneMatch.Add(etag);

        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Two_realms_do_not_share_an_etag()
    {
        // The tag is a hash of the bytes. A tag derived from the build instead would be equal
        // across every sheet, and a client that had one map would be told it had all of them.
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterAsync(client);

        var sheets = await client.GetFromJsonAsync<List<Sheet>>(
            new Uri("/api/maps", UriKind.Relative));

        Assert.NotNull(sheets);
        Assert.True(sheets.Count > 1, "This assertion needs at least two realms to compare.");

        var tags = new List<EntityTagHeaderValue>();
        foreach (var sheet in sheets)
        {
            var response = await client.GetAsync(new Uri($"/api/maps/{sheet.World}", UriKind.Relative));
            tags.Add(response.Headers.ETag!);
        }

        Assert.Equal(tags.Count, tags.Select(t => t.Tag.ToString()).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task A_world_with_no_map_is_a_named_404_rather_than_an_empty_image()
    {
        // An empty 200 would render as a broken image with nothing to read, and the answer to
        // "which worlds have maps" is the list beside this route.
        using var client = NewClient(postgres.App);
        await BuilderClient.RegisterAsync(client);

        var response = await client.GetAsync(new Uri("/api/maps/nosuchrealm", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("nosuchrealm", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_sheets_are_embedded_in_the_assembly_rather_than_read_from_disk()
    {
        // The container publishes /app/publish and nothing else - there is no content/ directory
        // beside the server in the image. If this ever regresses to a file read it will pass on a
        // developer's machine and serve nothing at all in production, which is exactly the class
        // of failure the canon is embedded to avoid.
        var sheets = new MapSheets();

        Assert.NotEmpty(sheets.All);
        Assert.True(sheets.TryGet(sheets.All[0].World, out var svg, out _));
        Assert.NotEmpty(svg);
    }

    [Fact]
    public void A_world_key_is_matched_without_regard_to_case()
    {
        // Room keys are lowercase by construction, but this is reached from a URL somebody can
        // type, and "no map of Ossara" is a confusing thing to be told while standing in it.
        var sheets = new MapSheets();
        var world = sheets.All[0].World;

        Assert.True(sheets.TryGet(world.ToUpperInvariant(), out _, out _));
    }
}
