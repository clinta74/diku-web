using System.Diagnostics.CodeAnalysis;

namespace DikuWeb.Domain.Worlds;

/// <summary>
/// A room address in "world.zone.room" form, e.g. "aldenmoor.millbrook.north-gate"
/// (PLAN.md §4.1). Human-readable keys make content diffs and audit rows reviewable in a
/// way integer vnums never are.
/// </summary>
/// <remarks>
/// A value object rather than a bare string because room keys are passed everywhere - exits,
/// character locations, spawners, quests - and a two-segment key or a stray uppercase letter
/// would otherwise fail silently as a room that simply never resolves.
/// </remarks>
public readonly record struct RoomKey
{
    public const int MaxLength = 128;

    private RoomKey(string world, string zone, string room)
    {
        World = world;
        Zone = zone;
        Room = room;
    }

    public string World { get; }

    public string Zone { get; }

    public string Room { get; }

    /// <summary>The owning zone's key, "world.zone".</summary>
    public string ZoneKey => $"{World}.{Zone}";

    public string WorldKey => World;

    public bool IsEmpty => World is null;

    public override string ToString() => $"{World}.{Zone}.{Room}";

    public static RoomKey Parse(string value) =>
        TryParse(value, out var key)
            ? key
            : throw new FormatException($"'{value}' is not a valid room key (expected world.zone.room).");

    public static bool TryParse([NotNullWhen(true)] string? value, out RoomKey key)
    {
        key = default;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!IsValidSegment(part))
            {
                return false;
            }
        }

        key = new RoomKey(parts[0], parts[1], parts[2]);
        return true;
    }

    public static RoomKey Create(string world, string zone, string room) =>
        Parse($"{world}.{zone}.{room}");

    /// <summary>
    /// Lowercase letters, digits, and hyphens. Deliberately narrow: keys appear in URLs and
    /// in player-visible builder output, and case-insensitive comparison bugs are avoided
    /// entirely by not permitting uppercase in the first place.
    /// </summary>
    private static bool IsValidSegment(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty)
        {
            return false;
        }

        // A leading or trailing hyphen reads as a typo and would produce ugly keys.
        if (segment[0] == '-' || segment[^1] == '-')
        {
            return false;
        }

        foreach (var c in segment)
        {
            var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}
