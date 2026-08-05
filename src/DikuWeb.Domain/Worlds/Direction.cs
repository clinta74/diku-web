namespace DikuWeb.Domain.Worlds;

/// <summary>
/// Room-to-room movement only. There is no in-room movement (PLAN.md §4.2), so this is the
/// complete set of ways to leave a room.
/// </summary>
public enum Direction
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
    Up = 4,
    Down = 5,
}

public static class DirectionExtensions
{
    /// <summary>Display order: n, e, s, w, u, d. Conventional for MUD exit lines.</summary>
    public static readonly IReadOnlyList<Direction> All =
    [
        Direction.North,
        Direction.East,
        Direction.South,
        Direction.West,
        Direction.Up,
        Direction.Down,
    ];

    public static Direction Opposite(this Direction direction) => direction switch
    {
        Direction.North => Direction.South,
        Direction.South => Direction.North,
        Direction.East => Direction.West,
        Direction.West => Direction.East,
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    public static string ToLowerName(this Direction direction) => direction switch
    {
        Direction.North => "north",
        Direction.East => "east",
        Direction.South => "south",
        Direction.West => "west",
        Direction.Up => "up",
        Direction.Down => "down",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    /// <summary>The single-letter form a player actually types.</summary>
    public static string Abbreviation(this Direction direction) =>
        direction.ToLowerName()[..1];

    /// <summary>
    /// Accepts the full name or any unambiguous prefix, so "n", "no", and "north" all work.
    /// </summary>
    public static bool TryParse(string? input, out Direction direction)
    {
        direction = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var candidate = input.Trim().ToLowerInvariant();

        foreach (var value in All)
        {
            if (value.ToLowerName().StartsWith(candidate, StringComparison.Ordinal))
            {
                direction = value;
                return true;
            }
        }

        return false;
    }
}
