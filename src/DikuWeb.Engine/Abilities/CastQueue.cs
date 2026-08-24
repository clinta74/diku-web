namespace DikuWeb.Engine.Abilities;

/// <summary>
/// An ability cast in progress, awaiting its cast time to elapse before effect resolves.
/// </summary>
public sealed class CastJob
{
    /// <summary>Character ID of the caster.</summary>
    public required Guid CharacterId { get; init; }

    /// <summary>Ability key being cast (e.g., "warden.slash").</summary>
    public required string AbilityKey { get; init; }

    /// <summary>Entity ID of the target, or null for self/AoE.</summary>
    public string? TargetId { get; init; }

    /// <summary>The game pulse at which this cast resolves.</summary>
    public required long ResolveAtPulse { get; init; }

    /// <summary>The room where the cast started (for movement interruption check).</summary>
    public required string StartingRoomKey { get; init; }

    /// <summary>Optional narration about what's being cast (for interruption messages).</summary>
    public string? CastingDescription { get; init; }
}

/// <summary>
/// Queue for casts awaiting cast-time resolution.
/// Managed by AbilitySystem on each game loop tick.
/// </summary>
public sealed class CastQueueService
{
    private readonly List<CastJob> _pending = [];

    public IReadOnlyList<CastJob> Pending => _pending.AsReadOnly();

    public void Enqueue(CastJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        _pending.Add(job);
    }

    public void Remove(CastJob job) => _pending.Remove(job);

    public IReadOnlyList<CastJob> GetReadyToResolve(long currentPulse) =>
        _pending.Where(x => x.ResolveAtPulse <= currentPulse).ToList();

    public IReadOnlyList<CastJob> GetPendingForCharacter(Guid characterId) =>
        _pending.Where(x => x.CharacterId == characterId).ToList();

    /// <summary>
    /// Whether this character is mid-cast right now. Combat asks every pulse, so it answers
    /// without allocating the way <see cref="GetPendingForCharacter"/> does.
    /// </summary>
    public bool IsCasting(Guid characterId) =>
        _pending.Any(x => x.CharacterId == characterId);

    /// <summary>
    /// Drops every cast this character has in flight and returns them, so the caller can narrate
    /// what was lost. Used when a blow breaks concentration.
    /// </summary>
    public IReadOnlyList<CastJob> CancelFor(Guid characterId)
    {
        var cancelled = _pending.Where(x => x.CharacterId == characterId).ToList();
        _pending.RemoveAll(x => x.CharacterId == characterId);
        return cancelled;
    }

    /// <summary>The one wording for a broken cast, shared by every path that can break one.</summary>
    /// <remarks>
    /// <b>Takes the cache rather than a finished name</b>, because both callers hold a
    /// <see cref="CastJob"/> and a CastJob carries the key. Asking them for a display name would
    /// put the same lookup in two places, and the version that skipped it is what shipped: "Your
    /// warden.rallying-cry was interrupted." — an internal identifier, in a sentence, at the one
    /// moment the player is most annoyed.
    ///
    /// Falls back to the key when the ability cannot be found, which is a bad line but a better
    /// one than "Your  was interrupted." An unknown key at least says which thing broke.
    /// </remarks>
    public static string InterruptedText(string abilityKey, AbilityCache? abilities) =>
        $"Your {abilities?.Get(abilityKey)?.Name ?? abilityKey} was interrupted.";

    public void Clear() => _pending.Clear();
}
