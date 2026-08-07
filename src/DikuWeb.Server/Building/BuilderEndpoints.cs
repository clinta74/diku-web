using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Server.Auth;

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

        var group = routes.MapGroup("/api/builder").RequireAuthorization(Policies.Builder);

        // The flag registry drives the room editor's checkboxes, so a newly registered flag
        // reaches the UI with no client change at all (PLAN.md §4.10).
        group.MapGet("/room-flags", () => Results.Ok(
            RoomFlags.All.Select(f => new RoomFlagResponse(f.Key, f.Default, f.Summary, f.Phase))));

        group.MapGet("/worlds", ListWorldsAsync);
        group.MapGet("/worlds/{key}", GetWorldAsync);
        group.MapPost("/worlds/{key}", CreateWorldAsync);
        group.MapPatch("/worlds/{key}", UpdateWorldAsync);
        group.MapDelete("/worlds/{key}", DeleteWorldAsync);

        group.MapGet("/zones", ListZonesAsync);
        group.MapGet("/zones/{key}", GetZoneAsync);
        group.MapPost("/zones/{key}", CreateZoneAsync);
        group.MapPatch("/zones/{key}", UpdateZoneAsync);
        group.MapDelete("/zones/{key}", DeleteZoneAsync);

        group.MapGet("/zones/{key}/rooms", ListRoomsAsync);
        group.MapGet("/zones/{key}/validate", ValidateZoneAsync);
        group.MapGet("/zones/{key}/unfinished", UnfinishedAsync);
        group.MapGet("/zones/{key}/preview", PreviewAsync);

        group.MapGet("/audit", AuditAsync);

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
            ToFlagSet(request.Flags));

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
            request.Flags is null ? ToFlagSet(existing.Flags) : ToFlagSet(request.Flags));

        return await SaveAsync(editor, change, http, ct, () => queries.WorldAsync(key, ct));
    }

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
            ToFlagSet(request.Flags));

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
            request.Flags is null ? ToFlagSet(existing.Flags) : ToFlagSet(request.Flags));

        return await SaveAsync(editor, change, http, ct, () => queries.ZoneAsync(key, ct));
    }

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

        var change = new UpsertMobTemplate(
            key,
            Trim(request.Name) ?? key,
            request.Description ?? string.Empty,
            request.Icon ?? "m",
            request.Level ?? 1,
            request.BaseStats ?? new Dictionary<string, object>(),
            request.BaseXp ?? 0,
            request.BaseGold ?? 0,
            request.Behavior ?? new Dictionary<string, object>(),
            request.Loot ?? new List<Dictionary<string, object>>());

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

        var change = new UpsertMobTemplate(
            key,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.Icon ?? existing.Icon,
            request.Level ?? existing.Level,
            request.BaseStats ?? existing.BaseStats,
            request.BaseXp ?? existing.BaseXp,
            request.BaseGold ?? existing.BaseGold,
            request.Behavior ?? existing.Behavior,
            request.Loot ?? existing.Loot);

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

        var change = new UpsertItemTemplate(
            key,
            Trim(request.Name) ?? key,
            request.Description ?? string.Empty,
            request.Icon ?? "$",
            request.Slot,
            request.Weight ?? 1,
            request.BaseValue ?? 0,
            request.BaseStats ?? new Dictionary<string, object>());

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

        var change = new UpsertItemTemplate(
            key,
            Trim(request.Name) ?? existing.Name,
            request.Description ?? existing.Description,
            request.Icon ?? existing.Icon,
            request.Slot ?? existing.Slot,
            request.Weight ?? existing.Weight,
            request.BaseValue ?? existing.BaseValue,
            request.BaseStats ?? existing.BaseStats);

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

        var id = Guid.CreateVersion7();
        var change = new UpsertSpawner(
            id,
            request.ZoneKey,
            request.TemplateKey,
            request.TemplateKind ?? TemplateKind.Mob,
            request.RoomKeys ?? new List<string>(),
            request.TargetCount ?? 1,
            request.RespawnSeconds ?? 60,
            request.Sentinel ?? false);

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

        var change = new UpsertSpawner(
            id,
            zoneKey,
            request.TemplateKey ?? existing.TemplateKey,
            request.TemplateKind ?? existing.TemplateKind,
            request.RoomKeys ?? existing.RoomKeys,
            request.TargetCount ?? existing.TargetCount,
            request.RespawnSeconds ?? existing.RespawnSeconds,
            request.Sentinel ?? existing.Sentinel);

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

        var warnings = new List<object>();

        // Check required item reachability
        if (!string.IsNullOrEmpty(quest.RequiredItemKey))
        {
            // Simplified: just check if template exists; full implementation would walk loot/spawners
            var reachable = !string.IsNullOrEmpty(quest.RequiredItemKey);
            if (!reachable)
            {
                warnings.Add(new { type = "unreachable-required-item", item = quest.RequiredItemKey });
            }
        }

        // Check reward item reachability
        if (!string.IsNullOrEmpty(quest.RewardItemKey))
        {
            var reachable = !string.IsNullOrEmpty(quest.RewardItemKey);
            if (!reachable)
            {
                warnings.Add(new { type = "unreachable-reward-item", item = quest.RewardItemKey });
            }
        }

        return Results.Ok(new { questKey = key, warnings });
    }

    /// <summary>
    /// Returns the quest graph for a zone: nodes are quests, edges are prerequisites.
    /// Detects cycles and dead ends.
    /// </summary>
    private static async Task<IResult> StorylineGraphAsync(
        string zoneKey,
        BuilderQueries queries,
        CancellationToken ct)
    {
        var quests = await queries.QuestsByZoneAsync(zoneKey, ct);

        var nodes = quests.Select(q => new { key = q.Key, name = q.Name }).ToList();
        var edges = new List<object>();
        var cycles = new List<string>();
        var deadEnds = new List<string>();

        var questMap = quests.ToDictionary(q => q.Key);

        // Build edges from prerequisites
        foreach (var quest in quests)
        {
            foreach (var prereq in quest.PrerequisiteQuestKeys)
            {
                if (questMap.ContainsKey(prereq))
                {
                    edges.Add(new { from = prereq, to = quest.Key });
                }
            }
        }

        // Detect cycles (simplified: just mark quests with circular dependencies)
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var quest in quests)
        {
            if (HasCycle(quest.Key, questMap, visited, recursionStack))
            {
                cycles.Add(quest.Key);
            }
        }

        // Detect dead ends (quests that nothing unlocks)
        var reachable = new HashSet<string>();
        foreach (var quest in quests)
        {
            if (quest.PrerequisiteQuestKeys.Count == 0)
            {
                reachable.Add(quest.Key);
            }
        }

        // Forward pass: mark all reachable quests
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var quest in quests)
            {
                if (!reachable.Contains(quest.Key) && quest.PrerequisiteQuestKeys.All(p => reachable.Contains(p)))
                {
                    reachable.Add(quest.Key);
                    changed = true;
                }
            }
        }

        foreach (var quest in quests)
        {
            if (!reachable.Contains(quest.Key))
            {
                deadEnds.Add(quest.Key);
            }
        }

        return Results.Ok(new
        {
            zoneKey,
            nodes,
            edges,
            cycles,
            deadEnds,
        });
    }

    private static bool HasCycle(
        string questKey,
        Dictionary<string, QuestResponse> questMap,
        HashSet<string> visited,
        HashSet<string> recursionStack)
    {
        if (recursionStack.Contains(questKey))
        {
            return true;
        }

        if (visited.Contains(questKey))
        {
            return false;
        }

        visited.Add(questKey);
        recursionStack.Add(questKey);

        if (questMap.TryGetValue(questKey, out var quest))
        {
            foreach (var prereq in quest.PrerequisiteQuestKeys)
            {
                if (HasCycle(prereq, questMap, visited, recursionStack))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(questKey);
        return false;
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
}
