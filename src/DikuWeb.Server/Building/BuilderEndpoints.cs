using System.Text.Json;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Server.Auth;
using DikuWeb.Server.Infrastructure;
using Microsoft.AspNetCore.Http.Features;

namespace DikuWeb.Server.Building;

/// <summary>
/// The builder API (PLAN.md §7.3). Reads come from the database via <see cref="BuilderQueries"/>;
/// writes go through <see cref="WorldEditor"/>, which is the only path that touches the world.
/// </summary>
public static class BuilderEndpoints
{
    public static void MapBuilderEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Throttled per account, and loosely: opening the builder issues a burst of reads for the
        // zone tree, and the population here is a trusted role whose failure mode is mess rather
        // than breach — the same reasoning DigThrottle already carries for `dig` alone.
        var group = routes.MapGroup("/api/builder")
            .RequireAuthorization(Policies.Builder)
            .RequireRateLimiting(RateLimiting.Builder);

        // The flag registry drives the room editor's checkboxes, so a newly registered flag
        // reaches the UI with no client change at all (PLAN.md §4.10).
        group.MapGet("/room-flags", () => Results.Ok(
            RoomFlags.All.Select(f => new RoomFlagResponse(f.Key, f.Default, f.Summary, f.Phase))));

        group.MapGet("/worlds", ListWorldsAsync);
        group.MapGet("/worlds/{key}", GetWorldAsync);
        group.MapPost("/worlds/{key}", CreateWorldAsync);
        group.MapPatch("/worlds/{key}", UpdateWorldAsync);
        group.MapPut("/worlds/{key}/flags/{flag}", SetWorldFlagAsync);
        group.MapDelete("/worlds/{key}", DeleteWorldAsync);

        group.MapGet("/zones", ListZonesAsync);
        group.MapGet("/zones/{key}", GetZoneAsync);
        group.MapPost("/zones/{key}", CreateZoneAsync);
        group.MapPatch("/zones/{key}", UpdateZoneAsync);
        group.MapPut("/zones/{key}/flags/{flag}", SetZoneFlagAsync);
        group.MapDelete("/zones/{key}", DeleteZoneAsync);

        group.MapGet("/zones/{key}/rooms", ListRoomsAsync);
        group.MapGet("/zones/{key}/validate", ValidateZoneAsync);
        group.MapGet("/zones/{key}/unfinished", UnfinishedAsync);
        group.MapGet("/zones/{key}/preview", PreviewAsync);

        group.MapGet("/audit", AuditAsync);

        group.MapGet("/export", ExportAsync);
        group.MapPost("/import", ImportAsync);

        // Live edit feed: a second builder's saved change lands here so open panels refresh.
        group.MapGet("/stream", StreamChangesAsync);

        group.MapGet("/mob-templates", ListMobTemplatesAsync);
        group.MapGet("/mob-templates/{key}", GetMobTemplateAsync);
        group.MapPost("/mob-templates/{key}", CreateMobTemplateAsync);
        group.MapPatch("/mob-templates/{key}", UpdateMobTemplateAsync);
        group.MapDelete("/mob-templates/{key}", DeleteMobTemplateAsync);

        group.MapGet("/item-templates", ListItemTemplatesAsync);
        group.MapGet("/item-templates/{key}", GetItemTemplateAsync);
        group.MapPost("/item-templates/{key}", CreateItemTemplateAsync);
        group.MapPatch("/item-templates/{key}", UpdateItemTemplateAsync);
        group.MapDelete("/item-templates/{key}", DeleteItemTemplateAsync);

        group.MapGet("/spawners", ListSpawnersAsync);
        group.MapGet("/spawners/{id}", GetSpawnerAsync);
        group.MapPost("/spawners", CreateSpawnerAsync);
        group.MapPatch("/spawners/{id}", UpdateSpawnerAsync);
        group.MapDelete("/spawners/{id}", DeleteSpawnerAsync);

        group.MapGet("/quests", ListQuestsAsync);
        group.MapGet("/quests/{key}", GetQuestAsync);
        group.MapPost("/quests/{key}", CreateQuestAsync);
        group.MapPatch("/quests/{key}", UpdateQuestAsync);
        group.MapDelete("/quests/{key}", DeleteQuestAsync);
        group.MapGet("/quests/{key}/reachability", QuestReachabilityAsync);
        group.MapGet("/zones/{zoneKey}/storyline", StorylineGraphAsync);

        routes.MapBuilderRoomEndpoints();
    }

    // -----------------------------------------------------------------------
    // Change feed
    // -----------------------------------------------------------------------

    /// <summary>
    /// A long-lived SSE stream of persisted edits (PLAN §2). Modelled on the game stream, minus
    /// the per-character session: any authorised builder may listen and every subscriber sees
    /// every edit. Auth is the cookie, since EventSource cannot set headers.
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    private static async Task StreamChangesAsync(
        HttpContext http,
        BuilderChangeFeed feed,
        CancellationToken ct)
    {
        var response = http.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        // Covers nginx (header) and Kestrel (DisableBuffering) - otherwise events sit buffered
        // and the stream looks dead.
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var subscription = feed.Subscribe(out var reader);

        try
        {
            await response.WriteAsync("retry: 3000\n\n", ct);
            await response.Body.FlushAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                // Wake at least every heartbeat so an idle stream stays alive through proxies.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(HeartbeatInterval);

                bool hasData;
                try
                {
                    hasData = await reader.WaitToReadAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await response.WriteAsync(": ping\n\n", ct);
                    await response.Body.FlushAsync(ct);
                    continue;
                }

                if (!hasData)
                {
                    break;
                }

                while (reader.TryRead(out var change))
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        kind = change.Kind,
                        key = change.Key,
                        action = change.Action,
                        byAccountId = change.ByAccountId,
                    });
                    await response.WriteAsync($"event: entity-changed\ndata: {json}\n\n", ct);
                }

                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The client went away. Expected; the subscription is disposed by the using above.
        }
    }

    // -----------------------------------------------------------------------
    // Worlds
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListWorldsAsync(BuilderQueries queries, CancellationToken ct) =>
        Results.Ok(await queries.WorldsAsync(ct));

    private static async Task<IResult> GetWorldAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.WorldAsync(key, ct) is { } world ? Results.Ok(world) : Results.NotFound();

    private static async Task<IResult> CreateWorldAsync(
        string key,
        SaveWorldRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsKeySegment(key))
        {
            return Invalid("A world key must be lowercase letters, digits, or hyphens.");
        }

        if (await queries.WorldAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"World '{key}' already exists." });
        }

        var change = new UpsertWorld(
            key,
            Trim(request.Name) ?? key,
            request.Description ?? string.Empty,
            request.SortOrder ?? 0,
            ToFlagSet(request.Flags),
            request.Multipliers ?? new Multipliers());

        return await SaveAsync(editor, change, http, ct, () => queries.WorldAsync(key, ct));
    }

    private static async Task<IResult> UpdateWorldAsync(
        string key,
        SaveWorldRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.WorldAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var change = new UpsertWorld(
            key,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.SortOrder ?? existing.SortOrder,
            // A null Flags means "leave them alone"; an empty object means "clear them". The
            // distinction matters because PATCH bodies are routinely partial.
            request.Flags is null ? ToFlagSet(existing.Flags) : ToFlagSet(request.Flags),
            request.Multipliers ?? existing.Multipliers);

        return await SaveAsync(editor, change, http, ct, () => queries.WorldAsync(key, ct));
    }

    /// <summary>
    /// Sets one flag on one world, leaving its siblings alone.
    /// </summary>
    /// <remarks>
    /// The room editor has had this since §4.10; the scopes above it were still doing a
    /// read-modify-PATCH of the whole flag map, which loses a concurrent builder's edit to a
    /// different flag - and loses it silently, since the second write is a perfectly valid map.
    /// The blast radius is largest here, so the narrow primitive matters most here.
    ///
    /// A null value clears the key rather than setting it false, which is the third state the
    /// builder's control offers: let the level above decide.
    /// </remarks>
    private static async Task<IResult> SetWorldFlagAsync(
        string key,
        string flag,
        SetFlagRequest request,
        WorldEditor editor,
        BuilderQueries queries,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(
            editor,
            new SetWorldFlag(key, flag, request.Value),
            http,
            ct,
            () => queries.WorldAsync(key, ct));

    private static async Task<IResult> DeleteWorldAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteWorld(key), http, ct, () => Task.FromResult<object?>(null));

    // -----------------------------------------------------------------------
    // Zones
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListZonesAsync(
        string? world,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.ZonesAsync(world, ct));

    private static async Task<IResult> GetZoneAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.ZoneAsync(key, ct) is { } zone ? Results.Ok(zone) : Results.NotFound();

    private static async Task<IResult> CreateZoneAsync(
        string key,
        SaveZoneRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        var parts = key.Split('.');
        if (parts.Length != 2 || !parts.All(IsKeySegment))
        {
            return Invalid("A zone key must be 'world.zone'.");
        }

        if (await queries.ZoneAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"Zone '{key}' already exists." });
        }

        var worldKey = Trim(request.WorldKey) ?? parts[0];

        var change = new UpsertZone(
            key,
            worldKey,
            Trim(request.Name) ?? parts[1],
            request.Description ?? string.Empty,
            request.MinLevel ?? 1,
            request.MaxLevel ?? 50,
            ToFlagSet(request.Flags),
            request.Multipliers ?? new Multipliers());

        return await SaveAsync(editor, change, http, ct, () => queries.ZoneAsync(key, ct));
    }

    private static async Task<IResult> UpdateZoneAsync(
        string key,
        SaveZoneRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.ZoneAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var change = new UpsertZone(
            key,
            existing.WorldKey,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.MinLevel ?? existing.MinLevel,
            request.MaxLevel ?? existing.MaxLevel,
            request.Flags is null ? ToFlagSet(existing.Flags) : ToFlagSet(request.Flags),
            request.Multipliers ?? existing.Multipliers);

        return await SaveAsync(editor, change, http, ct, () => queries.ZoneAsync(key, ct));
    }

    /// <inheritdoc cref="SetWorldFlagAsync"/>
    private static async Task<IResult> SetZoneFlagAsync(
        string key,
        string flag,
        SetFlagRequest request,
        WorldEditor editor,
        BuilderQueries queries,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(
            editor,
            new SetZoneFlag(key, flag, request.Value),
            http,
            ct,
            () => queries.ZoneAsync(key, ct));

    private static async Task<IResult> DeleteZoneAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteZone(key), http, ct, () => Task.FromResult<object?>(null));

    private static async Task<IResult> ListRoomsAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.RoomsAsync(key, ct));

    private static async Task<IResult> ValidateZoneAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.ValidateAsync(key, ct));

    private static async Task<IResult> UnfinishedAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.UnfinishedAsync(key, ct));

    private static async Task<IResult> PreviewAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.PreviewAsync(key, ct) is { } preview
            ? Results.Ok(preview)
            : Results.NotFound();

    private static async Task<IResult> AuditAsync(
        string? kind,
        string? key,
        int? limit,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.AuditAsync(kind, key, limit ?? 50, ct));

    // -----------------------------------------------------------------------
    // Export and import (PLAN.md §6, Phase 6)
    // -----------------------------------------------------------------------

    /// <summary>
    /// The authored world as one JSON document: everything, or one world, or one zone.
    /// </summary>
    /// <remarks>
    /// Sent as an attachment with a dated filename, because the thing a builder does with this is
    /// save it - and a bundle that arrives as an untitled browser tab is one nobody keeps.
    /// </remarks>
    private static async Task<IResult> ExportAsync(
        string? world,
        string? zone,
        WorldExporter exporter,
        HttpContext http,
        CancellationToken ct)
    {
        if (await exporter.ExportAsync(world, zone, ct) is not { } bundle)
        {
            return Results.NotFound(new
            {
                error = string.IsNullOrWhiteSpace(zone)
                    ? $"No world '{world}'."
                    : $"No zone '{zone}'.",
            });
        }

        var name = bundle.Scope.Key ?? "world";
        http.Response.Headers.ContentDisposition =
            $"attachment; filename=\"{name}-{bundle.ExportedAt:yyyy-MM-dd}.json\"";

        return Results.Ok(bundle);
    }

    /// <summary>
    /// Applies a bundle to this environment. <c>?dryRun=true</c> reports what would happen and
    /// changes nothing.
    /// </summary>
    /// <remarks>
    /// The format version is the one hard refusal here. Everything else - a dangling exit, a
    /// quest whose giver is somewhere else - comes back as an advisory warning, because those are
    /// states the world already tolerates (§7.4) and refusing them would make importing one zone
    /// of several impossible.
    /// </remarks>
    private static async Task<IResult> ImportAsync(
        WorldBundle? bundle,
        bool? dryRun,
        WorldImporter importer,
        HttpContext http,
        CancellationToken ct)
    {
        if (bundle is null)
        {
            return Invalid("An import needs a bundle.");
        }

        if (bundle.FormatVersion != WorldBundle.CurrentFormatVersion)
        {
            return Invalid(
                $"This is a version {bundle.FormatVersion} bundle; this server reads version "
                + $"{WorldBundle.CurrentFormatVersion}.");
        }

        http.TryGetAccountId(out var accountId);

        var report = await importer.ImportAsync(bundle, accountId, dryRun ?? false, ct);

        // A partial import is not a success, and reporting one as 200 is how a half-applied zone
        // gets noticed a week later. The report is the body either way, since which entities
        // failed is the whole answer.
        return report.Ok
            ? Results.Ok(report)
            : Results.Json(report, statusCode: StatusCodes.Status207MultiStatus);
    }

    // -----------------------------------------------------------------------
    // Mob Templates
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListMobTemplatesAsync(
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.MobTemplatesAsync(ct));

    private static async Task<IResult> GetMobTemplateAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.MobTemplateAsync(key, ct) is { } template ? Results.Ok(template) : Results.NotFound();

    private static async Task<IResult> CreateMobTemplateAsync(
        string key,
        SaveMobTemplateRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsKeySegment(key))
        {
            return Invalid("A mob template key must be lowercase letters, digits, or hyphens.");
        }

        if (await queries.MobTemplateAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"Mob template '{key}' already exists." });
        }

        if (ValidateAttacks(request.Attacks) is { } refusal)
        {
            return refusal;
        }

        var change = new UpsertMobTemplate(
            key,
            Trim(request.Name) ?? key,
            request.Description ?? string.Empty,
            request.Icon ?? "m",
            request.Level ?? 1,
            request.WanderIntervalPulses ?? 24,
            request.BaseStats ?? new Dictionary<string, object>(),
            request.BaseXp ?? 0,
            request.BaseGold ?? 0,
            request.Behavior ?? new Dictionary<string, object>(),
            request.Loot ?? new List<Dictionary<string, object>>(),
            NormalizeAttacks(request.Attacks) ?? new List<MobAttack>());

        return await SaveAsync(editor, change, http, ct, () => queries.MobTemplateAsync(key, ct));
    }

    private static async Task<IResult> UpdateMobTemplateAsync(
        string key,
        SaveMobTemplateRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.MobTemplateAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        if (ValidateAttacks(request.Attacks) is { } refusal)
        {
            return refusal;
        }

        var change = new UpsertMobTemplate(
            key,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.Icon ?? existing.Icon,
            request.Level ?? existing.Level,
            request.WanderIntervalPulses ?? existing.WanderIntervalPulses,
            request.BaseStats ?? existing.BaseStats,
            request.BaseXp ?? existing.BaseXp,
            request.BaseGold ?? existing.BaseGold,
            request.Behavior ?? existing.Behavior,
            request.Loot ?? existing.Loot,
            NormalizeAttacks(request.Attacks) ?? existing.Attacks);

        return await SaveAsync(editor, change, http, ct, () => queries.MobTemplateAsync(key, ct));
    }

    private static async Task<IResult> DeleteMobTemplateAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteMobTemplate(key), http, ct, () => Task.FromResult<object?>(null));

    // -----------------------------------------------------------------------
    // Item Templates
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListItemTemplatesAsync(
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.ItemTemplatesAsync(ct));

    private static async Task<IResult> GetItemTemplateAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.ItemTemplateAsync(key, ct) is { } template ? Results.Ok(template) : Results.NotFound();

    private static async Task<IResult> CreateItemTemplateAsync(
        string key,
        SaveItemTemplateRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsKeySegment(key))
        {
            return Invalid("An item template key must be lowercase letters, digits, or hyphens.");
        }

        if (await queries.ItemTemplateAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"Item template '{key}' already exists." });
        }

        if (ValidateWeapon(request.AttackDelayPulses, request.AttackVerb) is { } refusal)
        {
            return refusal;
        }

        var change = new UpsertItemTemplate(
            key,
            Trim(request.Name) ?? key,
            request.Description ?? string.Empty,
            request.Icon ?? "$",
            request.Slot,
            request.Weight ?? 1,
            request.BaseValue ?? 0,
            request.BaseStats ?? new Dictionary<string, object>(),
            request.AttackDelayPulses,
            Trim(request.AttackVerb),
            request.IsQuestItem ?? false);

        return await SaveAsync(editor, change, http, ct, () => queries.ItemTemplateAsync(key, ct));
    }

    private static async Task<IResult> UpdateItemTemplateAsync(
        string key,
        SaveItemTemplateRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.ItemTemplateAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        if (ValidateWeapon(request.AttackDelayPulses, request.AttackVerb) is { } refusal)
        {
            return refusal;
        }

        var change = new UpsertItemTemplate(
            key,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.Icon ?? existing.Icon,
            request.Slot ?? existing.Slot,
            request.Weight ?? existing.Weight,
            request.BaseValue ?? existing.BaseValue,
            request.BaseStats ?? existing.BaseStats,
            request.AttackDelayPulses ?? existing.AttackDelayPulses,
            Trim(request.AttackVerb) ?? existing.AttackVerb,
            request.IsQuestItem ?? existing.IsQuestItem);

        return await SaveAsync(editor, change, http, ct, () => queries.ItemTemplateAsync(key, ct));
    }

    private static async Task<IResult> DeleteItemTemplateAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteItemTemplate(key), http, ct, () => Task.FromResult<object?>(null));

    // -----------------------------------------------------------------------
    // Spawners
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListSpawnersAsync(
        string? zone,
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.SpawnersAsync(zone, ct));

    private static async Task<IResult> GetSpawnerAsync(
        Guid id,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.SpawnerAsync(id, ct) is { } spawner ? Results.Ok(spawner) : Results.NotFound();

    private static async Task<IResult> CreateSpawnerAsync(
        SaveSpawnerRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ZoneKey))
        {
            return Invalid("Zone key is required.");
        }

        if (await queries.ZoneAsync(request.ZoneKey, ct) is null)
        {
            return Invalid($"No zone '{request.ZoneKey}'.");
        }

        if (string.IsNullOrWhiteSpace(request.TemplateKey))
        {
            return Invalid("Template key is required.");
        }

        // Absent means "follow the template", which is the default a fresh spawner should have.
        if (!WanderMode.TryParse(request.Wander ?? WanderMode.Template, out var wanders))
        {
            return Invalid($"Unknown wander mode '{request.Wander}'.");
        }

        var id = Guid.CreateVersion7();
        var change = new UpsertSpawner(
            id,
            request.ZoneKey,
            request.TemplateKey,
            request.TemplateKind ?? TemplateKind.Mob,
            request.RoomKeys ?? new List<string>(),
            request.TargetCount ?? 1,
            request.RespawnSeconds ?? 60,
            wanders);

        return await SaveAsync(editor, change, http, ct, () => queries.SpawnerAsync(id, ct));
    }

    private static async Task<IResult> UpdateSpawnerAsync(
        Guid id,
        SaveSpawnerRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.SpawnerAsync(id, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var zoneKey = request.ZoneKey ?? existing.ZoneKey;
        if (await queries.ZoneAsync(zoneKey, ct) is null)
        {
            return Invalid($"No zone '{zoneKey}'.");
        }

        // An absent Wander leaves the stored answer alone, exactly as an absent TargetCount does.
        // This is why the wire carries a word: the stored value is itself nullable, so a nullable
        // bool here could not tell "leave it" from "follow the template" (see WanderMode).
        if (!WanderMode.TryParse(request.Wander ?? existing.Wander, out var wanders))
        {
            return Invalid($"Unknown wander mode '{request.Wander}'.");
        }

        var change = new UpsertSpawner(
            id,
            zoneKey,
            request.TemplateKey ?? existing.TemplateKey,
            request.TemplateKind ?? existing.TemplateKind,
            request.RoomKeys ?? existing.RoomKeys,
            request.TargetCount ?? existing.TargetCount,
            request.RespawnSeconds ?? existing.RespawnSeconds,
            wanders);

        return await SaveAsync(editor, change, http, ct, () => queries.SpawnerAsync(id, ct));
    }

    private static async Task<IResult> DeleteSpawnerAsync(
        Guid id,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteSpawner(id), http, ct, () => Task.FromResult<object?>(null));

    // -----------------------------------------------------------------------
    // Quests (Phase 5.2b)
    // -----------------------------------------------------------------------

    private static async Task<IResult> ListQuestsAsync(
        BuilderQueries queries,
        CancellationToken ct) =>
        Results.Ok(await queries.QuestsAsync(ct));

    private static async Task<IResult> GetQuestAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct) =>
        await queries.QuestAsync(key, ct) is { } quest ? Results.Ok(quest) : Results.NotFound();

    private static async Task<IResult> CreateQuestAsync(
        string key,
        SaveQuestRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (!IsKeySegment(key))
        {
            return Invalid("A quest key must be lowercase letters, digits, or hyphens.");
        }

        if (await queries.QuestAsync(key, ct) is not null)
        {
            return Results.Conflict(new { error = $"Quest '{key}' already exists." });
        }

        if (string.IsNullOrWhiteSpace(request.ZoneKey))
        {
            return Invalid("Quest zone is required.");
        }

        if (string.IsNullOrWhiteSpace(request.GiverMobKey))
        {
            return Invalid("Quest giver mob is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TurninMobKey))
        {
            return Invalid("Quest turnin mob is required.");
        }

        var change = new UpsertQuest(
            key,
            request.ZoneKey,
            Trim(request.Name) ?? key,
            request.Summary ?? string.Empty,
            request.Description ?? string.Empty,
            request.GiverMobKey,
            request.TurninMobKey,
            request.RequiredItemKey,
            request.RequiredCount ?? 1,
            request.RewardXp ?? 0,
            request.RewardGold ?? 0,
            request.RewardItemKey,
            request.RewardItemCount ?? 1,
            request.PrerequisiteQuestKeys ?? [],
            request.IsRepeatable ?? false,
            request.AutoStart ?? false,
            request.Dialogue ?? [],
            request.SortOrder ?? 0);

        return await SaveAsync(editor, change, http, ct, () => queries.QuestAsync(key, ct));
    }

    private static async Task<IResult> UpdateQuestAsync(
        string key,
        SaveQuestRequest request,
        BuilderQueries queries,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct)
    {
        if (await queries.QuestAsync(key, ct) is not { } existing)
        {
            return Results.NotFound();
        }

        var change = new UpsertQuest(
            key,
            request.ZoneKey ?? existing.ZoneKey,
            Trim(request.Name) ?? existing.Name,
            request.Summary ?? existing.Summary,
            request.Description ?? existing.Description,
            request.GiverMobKey ?? existing.GiverMobKey,
            request.TurninMobKey ?? existing.TurninMobKey,
            request.RequiredItemKey ?? existing.RequiredItemKey,
            request.RequiredCount ?? existing.RequiredCount,
            request.RewardXp ?? existing.RewardXp,
            request.RewardGold ?? existing.RewardGold,
            request.RewardItemKey ?? existing.RewardItemKey,
            request.RewardItemCount ?? existing.RewardItemCount,
            request.PrerequisiteQuestKeys ?? existing.PrerequisiteQuestKeys,
            request.IsRepeatable ?? existing.IsRepeatable,
            request.AutoStart ?? existing.AutoStart,
            request.Dialogue ?? existing.Dialogue,
            request.SortOrder ?? existing.SortOrder);

        return await SaveAsync(editor, change, http, ct, () => queries.QuestAsync(key, ct));
    }

    private static async Task<IResult> DeleteQuestAsync(
        string key,
        WorldEditor editor,
        HttpContext http,
        CancellationToken ct) =>
        await SaveAsync(editor, new DeleteQuest(key), http, ct, () => Task.FromResult<object?>(null));

    /// <summary>
    /// Returns reachability status for quest items: required and reward items.
    /// An item is reachable if it has at least one source (mob drop or spawner).
    /// </summary>
    private static async Task<IResult> QuestReachabilityAsync(
        string key,
        BuilderQueries queries,
        CancellationToken ct)
    {
        var quest = await queries.QuestAsync(key, ct);
        if (quest is null)
        {
            return Results.NotFound();
        }

        var mobs = await queries.MobTemplatesAsync(ct);
        var spawners = await queries.SpawnersAsync(null, ct);
        var quests = await queries.QuestsAsync(ct);
        var warnings = new List<ReachabilityWarning>();

        var spawnedMobs = spawners
            .Where(s => s.TemplateKind == TemplateKind.Mob)
            .Select(s => s.TemplateKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A quest nobody can start or hand in is as broken as one whose item does not drop, and
        // fails just as quietly - the NPC simply never appears.
        CheckMob(quest.GiverMobKey, "giver", "offer this quest");
        CheckMob(quest.TurninMobKey, "turnin", "accept the turn-in");

        if (!string.IsNullOrEmpty(quest.RequiredItemKey))
        {
            await CheckItemAsync(quest.RequiredItemKey, "required");
        }

        if (!string.IsNullOrEmpty(quest.RewardItemKey))
        {
            await CheckItemAsync(quest.RewardItemKey, "reward");
        }

        return Results.Ok(new QuestReachability(key, warnings));

        void CheckMob(string mobKey, string role, string action)
        {
            if (string.IsNullOrEmpty(mobKey))
            {
                return;
            }

            if (!mobs.Any(m => string.Equals(m.Key, mobKey, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(new ReachabilityWarning(
                    $"missing-{role}-mob",
                    $"No mob template '{mobKey}', so nothing can {action}.",
                    MobKey: mobKey));
                return;
            }

            if (!spawnedMobs.Contains(mobKey))
            {
                warnings.Add(new ReachabilityWarning(
                    $"unspawned-{role}-mob",
                    $"'{mobKey}' exists but no spawner places it, so it will never {action}.",
                    MobKey: mobKey));
            }
        }

        // §10: prove the item has at least one source rather than assuming it. An unobtainable
        // quest item is the classic silent quest bug - the editor reads fine and the player just
        // wanders - so this walks loot tables, spawners, and other quests' rewards.
        async Task CheckItemAsync(string itemKey, string role)
        {
            if (await queries.ItemTemplateAsync(itemKey, ct) is null)
            {
                warnings.Add(new ReachabilityWarning(
                    $"missing-{role}-item",
                    $"No item template '{itemKey}'.",
                    ItemKey: itemKey));
                return;
            }

            // A reward is granted directly by the turn-in, so it needs no world source.
            if (role == "reward")
            {
                return;
            }

            var droppers = mobs
                .Where(m => DropsItem(m, itemKey))
                .Select(m => m.Key)
                .ToList();

            var spawnsIt = spawners.Any(s =>
                s.TemplateKind == TemplateKind.Item
                && string.Equals(s.TemplateKey, itemKey, StringComparison.OrdinalIgnoreCase));

            var rewardedBy = quests.Any(q =>
                !string.Equals(q.Key, key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(q.RewardItemKey, itemKey, StringComparison.OrdinalIgnoreCase));

            if (droppers.Count == 0 && !spawnsIt && !rewardedBy)
            {
                warnings.Add(new ReachabilityWarning(
                    $"unreachable-{role}-item",
                    $"Nothing drops, spawns, or rewards '{itemKey}', so the quest cannot be finished.",
                    ItemKey: itemKey));
                return;
            }

            // Loot on a mob no spawner places is loot nobody can reach.
            if (!spawnsIt && !rewardedBy && droppers.All(m => !spawnedMobs.Contains(m)))
            {
                warnings.Add(new ReachabilityWarning(
                    $"unspawned-{role}-item-source",
                    $"'{itemKey}' only drops from {string.Join(", ", droppers)}, which no spawner places.",
                    ItemKey: itemKey));
            }
        }
    }

    /// <summary>
    /// Whether a mob's loot table lists an item with a non-zero chance. Mirrors how CombatSystem
    /// reads the same jsonb: an "itemTemplateKey" and a "chance" per entry.
    /// </summary>
    private static bool DropsItem(MobTemplateResponse mob, string itemKey)
    {
        foreach (var entry in mob.Loot)
        {
            if (!entry.TryGetValue("itemTemplateKey", out var keyValue))
            {
                continue;
            }

            if (!string.Equals(keyValue?.ToString(), itemKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A zero chance is a table entry that can never fire, so it is not a source.
            if (!entry.TryGetValue("chance", out var chanceValue))
            {
                return true;
            }

            return !double.TryParse(
                chanceValue?.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var chance) || chance > 0;
        }

        return false;
    }

    /// <summary>
    /// The quest graph for a zone: nodes are quests, edges are prerequisites, plus the two ways
    /// a chain can be broken - a cycle (§7.4: every quest in it is unstartable) and a quest whose
    /// prerequisites can never all be met.
    /// </summary>
    /// <remarks>
    /// Resolution spans every zone even though only this zone's quests are drawn. Prerequisites
    /// are plain keys and chains legitimately cross zones, so scoping the lookup to one zone
    /// dropped those edges and then reported the dependent quest as unreachable - a warning about
    /// a chain that was in fact fine.
    /// </remarks>
    private static async Task<IResult> StorylineGraphAsync(
        string zoneKey,
        BuilderQueries queries,
        CancellationToken ct)
    {
        var all = (await queries.QuestsAsync(ct)).ToDictionary(q => q.Key, StringComparer.Ordinal);
        var inZone = all.Values
            .Where(q => string.Equals(q.ZoneKey, zoneKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(q => q.SortOrder)
            .ToList();

        // Prerequisites outside the zone are drawn too, marked external, so a cross-zone chain
        // is visible rather than looking like a quest with no way in.
        var shown = new HashSet<string>(inZone.Select(q => q.Key), StringComparer.Ordinal);
        foreach (var quest in inZone)
        {
            foreach (var prereq in quest.PrerequisiteQuestKeys.Where(all.ContainsKey))
            {
                shown.Add(prereq);
            }
        }

        var nodes = shown
            .Select(k => all[k])
            .Select(q => new
            {
                key = q.Key,
                name = q.Name,
                zoneKey = q.ZoneKey,
                external = !string.Equals(q.ZoneKey, zoneKey, StringComparison.OrdinalIgnoreCase),
            })
            .ToList();

        var edges = inZone
            .SelectMany(q => q.PrerequisiteQuestKeys
                .Where(all.ContainsKey)
                .Select(p => new { from = p, to = q.Key }))
            .ToList();

        // A prerequisite naming a quest that does not exist can never be satisfied, which is a
        // different failure from a cycle and worth saying so.
        var missingPrerequisites = inZone
            .SelectMany(q => q.PrerequisiteQuestKeys
                .Where(p => !all.ContainsKey(p))
                .Select(p => new { quest = q.Key, missing = p }))
            .ToList();

        var onCycle = FindCycleMembers(all);

        // Reachable = every prerequisite is itself reachable. Runs over the whole graph so an
        // external prerequisite resolves properly, then reports only this zone's quests.
        var reachable = new HashSet<string>(
            all.Values.Where(q => q.PrerequisiteQuestKeys.Count == 0).Select(q => q.Key),
            StringComparer.Ordinal);

        bool changed;
        do
        {
            changed = false;
            foreach (var quest in all.Values)
            {
                if (reachable.Contains(quest.Key))
                {
                    continue;
                }

                if (quest.PrerequisiteQuestKeys.All(reachable.Contains))
                {
                    reachable.Add(quest.Key);
                    changed = true;
                }
            }
        }
        while (changed);

        return Results.Ok(new
        {
            zoneKey,
            nodes,
            edges,
            cycles = inZone.Where(q => onCycle.Contains(q.Key)).Select(q => q.Key).ToList(),
            unreachable = inZone.Where(q => !reachable.Contains(q.Key)).Select(q => q.Key).ToList(),
            missingPrerequisites,
        });
    }

    /// <summary>
    /// Every quest that sits on a prerequisite cycle, found in one pass.
    /// </summary>
    /// <remarks>
    /// The previous version shared one visited set across restarts while only clearing its
    /// recursion stack on the non-cycle path, so once any cycle was found the stack stayed dirty
    /// and later quests were reported as cyclic when they were not. Here the colour map is what
    /// carries across roots, and the stack is always unwound, so each node is classified once.
    /// </remarks>
    private static HashSet<string> FindCycleMembers(Dictionary<string, QuestResponse> all)
    {
        const int InProgress = 1;
        const int Done = 2;

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var onCycle = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        void Visit(string key)
        {
            if (!all.TryGetValue(key, out var quest))
            {
                return;
            }

            if (state.TryGetValue(key, out var colour))
            {
                if (colour == InProgress)
                {
                    // Everything from the earlier visit to the top of the path is in the cycle.
                    var start = path.LastIndexOf(key);
                    for (var i = start; i >= 0 && i < path.Count; i++)
                    {
                        onCycle.Add(path[i]);
                    }
                }

                return;
            }

            state[key] = InProgress;
            path.Add(key);

            foreach (var prereq in quest.PrerequisiteQuestKeys)
            {
                Visit(prereq);
            }

            path.RemoveAt(path.Count - 1);
            state[key] = Done;
        }

        foreach (var key in all.Keys)
        {
            Visit(key);
        }

        return onCycle;
    }

    // -----------------------------------------------------------------------
    // Shared plumbing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Runs an edit and turns the outcome into a response. Every builder write goes through
    /// here so the status mapping exists in exactly one place.
    /// </summary>
    internal static async Task<IResult> SaveAsync<T>(
        WorldEditor editor,
        WorldChange change,
        HttpContext http,
        CancellationToken ct,
        Func<Task<T>> reload)
    {
        http.TryGetAccountId(out var accountId);

        var outcome = await editor.ApplyAsync(change, accountId, ct);

        return outcome.Status switch
        {
            EditStatus.Saved => Results.Ok(await reload()),

            // Applied to memory, failed to persist, then rolled back by a world reload. The
            // builder is told plainly rather than being given a 200 for work that vanished.
            EditStatus.NotSaved => Results.Json(
                new { error = "The edit could not be saved and has been rolled back." },
                statusCode: StatusCodes.Status500InternalServerError),

            _ => Refused(outcome.Result),
        };
    }

    private static IResult Refused(MutationResult result) => result.Error switch
    {
        MutationError.NotFound => Results.NotFound(new { error = result.Message }),
        MutationError.Conflict or MutationError.Occupied =>
            Results.Conflict(new { error = result.Message }),
        _ => Results.BadRequest(new { error = result.Message }),
    };

    internal static IResult Invalid(string message) => Results.BadRequest(new { error = message });

    internal static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Turns the request's flat map into a <see cref="FlagSet"/>, dropping anything the
    /// registry does not recognise. Unknown keys are preserved when they come <em>from</em> the
    /// database, but there is no reason to let a client type a new one into existence - it
    /// would be a flag nothing ever reads (PLAN.md §4.10).
    /// </summary>
    internal static FlagSet ToFlagSet(IReadOnlyDictionary<string, bool>? flags)
    {
        var set = new FlagSet();

        if (flags is null)
        {
            return set;
        }

        foreach (var (key, value) in flags.Where(f => RoomFlags.IsKnown(f.Key)))
        {
            set.Set(key, value);
        }

        return set;
    }

    private static bool IsKeySegment(string value) =>
        !string.IsNullOrEmpty(value)
        && value[0] != '-'
        && value[^1] != '-'
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>Longest attack verb worth allowing - past this the combat log stops reading as prose.</summary>
    private const int MaxAttackVerbLength = 24;

    /// <summary>
    /// Refuses a weapon nobody could balance around. The engine clamps a too-fast delay anyway,
    /// but silently honouring a save that says 1 and running at 4 would leave a builder tuning a
    /// number the game ignores.
    /// </summary>
    private static IResult? ValidateWeapon(int? delayPulses, string? verb)
    {
        if (delayPulses is { } delay && delay < AttackTiming.MinDelayPulses)
        {
            return Invalid(
                $"An attack delay must be at least {AttackTiming.MinDelayPulses} pulses (1.0 second).");
        }

        return ValidateVerb(verb, "An attack verb");
    }

    private static IResult? ValidateVerb(string? verb, string subject)
    {
        if (verb is null)
        {
            return null;
        }

        var trimmed = verb.Trim();

        if (trimmed.Length > MaxAttackVerbLength)
        {
            return Invalid($"{subject} must be {MaxAttackVerbLength} characters or fewer.");
        }

        if (trimmed.Any(char.IsDigit))
        {
            return Invalid($"{subject} is a word, not a number - try \"slash\" or \"crush\".");
        }

        return null;
    }

    private static IResult? ValidateAttacks(List<MobAttack>? attacks)
    {
        if (attacks is null)
        {
            return null;
        }

        foreach (var attack in attacks)
        {
            if (attack is null)
            {
                continue;
            }

            if (attack.DelayPulses < AttackTiming.MinDelayPulses)
            {
                return Invalid(
                    $"An attack delay must be at least {AttackTiming.MinDelayPulses} pulses (1.0 second).");
            }

            if (attack.DamageMultiplier is <= 0m)
            {
                return Invalid("An attack's damage multiplier must be greater than zero.");
            }

            if (ValidateVerb(attack.Verb, "An attack message") is { } refusal)
            {
                return refusal;
            }

            // Refused rather than stored, the same way an unknown room flag is (§4.10). An effect
            // key nothing answers to would be a mob attack that silently swings for damage alone,
            // and the builder would have no way to tell that from an effect that simply missed.
            if (!string.IsNullOrWhiteSpace(attack.EffectKey) &&
                !KnownEffects.Contains(attack.EffectKey.Trim()))
            {
                return Invalid($"'{attack.EffectKey}' is not a known effect.");
            }
        }

        return null;
    }

    /// <summary>
    /// The effect executors that exist, for validating what a mob attack may carry.
    /// </summary>
    /// <remarks>
    /// The registry itself rather than a list of keys beside it: a second copy is how the
    /// catalogue test drifted, and this one would drift the same way the day an eighth executor
    /// lands. Stateless, so one shared instance is fine.
    /// </remarks>
    private static readonly EffectRegistry KnownEffects = new();

    /// <summary>
    /// Tidies an authored attack list. A blank verb defaults rather than being refused - the
    /// builder's row starts empty and there is no reason to make someone type "hit" to save.
    /// </summary>
    private static List<MobAttack>? NormalizeAttacks(List<MobAttack>? attacks) =>
        attacks is null
            ? null
            : [.. attacks.Where(a => a is not null).Select(a => new MobAttack
            {
                Verb = AttackTiming.VerbOr(a.Verb),
                DelayPulses = AttackTiming.Clamp(a.DelayPulses),
                DamageMultiplier = a.DamageMultiplier,

                // Carried through, not dropped. Rebuilding the row field by field is what makes
                // this the exact place a newly added property goes missing - silently, since the
                // save succeeds and only the effect is gone.
                EffectKey = string.IsNullOrWhiteSpace(a.EffectKey) ? null : a.EffectKey.Trim(),
                EffectParams = a.EffectParams is { Count: > 0 }
                    ? new Dictionary<string, string>(a.EffectParams, StringComparer.Ordinal)
                    : null,
            })];
}
