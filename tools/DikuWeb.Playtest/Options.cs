using DikuWeb.Playtest.Targets;

namespace DikuWeb.Playtest;

/// <summary>
/// What the run was asked to do.
/// </summary>
/// <remarks>
/// Hand-parsed rather than reached for a package. The surface is six flags, and this repo's
/// dependency list is short on purpose — one more package to keep current is a real cost against a
/// parser that fits on a screen.
/// </remarks>
public sealed record Options
{
    /// <summary>A running server to play against. Null means boot one.</summary>
    public Uri? Server { get; init; }

    /// <summary>Boot the server in-process against a throwaway database.</summary>
    public bool Hosted { get; init; }

    /// <summary>A plan file, or a directory of them.</summary>
    public string? Plans { get; init; }

    /// <summary>Where run directories are written.</summary>
    public string Output { get; init; } = "runs";

    /// <summary>An existing admin, for plans that need an elevated role.</summary>
    public AdminCredentials? Admin { get; init; }

    /// <summary>
    /// Skip a plan's <c>world:</c> fixtures and play the world as it stands.
    /// </summary>
    /// <remarks>
    /// For running against a world somebody built by hand, where the plan's own content would be
    /// a second version of what is already there under a different key. Nothing is created and
    /// nothing is checked — the plans simply meet whatever is in front of them.
    /// </remarks>
    public bool NoFixtures { get; init; }

    /// <summary>Print the transcript as it happens as well as writing it.</summary>
    public bool Follow { get; init; }

    /// <summary>
    /// Leave the characters this run created in the world.
    /// </summary>
    /// <remarks>
    /// Cleaning up is the default whenever an admin credential makes it possible, because the
    /// litter is real and compounds — but it is a deletion, so there has to be a way to say no.
    /// </remarks>
    public bool NoCleanup { get; init; }

    public static Options Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var options = new Options();
        string? adminUser = null;
        string? adminPassword = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--server":
                    options = options with { Server = new Uri(Next(args, ref i, "--server")) };
                    break;

                case "--hosted":
                    options = options with { Hosted = true };
                    break;

                case "--plans":
                    options = options with { Plans = Next(args, ref i, "--plans") };
                    break;

                case "--out":
                    options = options with { Output = Next(args, ref i, "--out") };
                    break;

                case "--admin-user":
                    adminUser = Next(args, ref i, "--admin-user");
                    break;

                case "--admin-password":
                    adminPassword = Next(args, ref i, "--admin-password");
                    break;

                case "--no-fixtures":
                    options = options with { NoFixtures = true };
                    break;

                case "--follow":
                    options = options with { Follow = true };
                    break;

                case "--no-cleanup":
                    options = options with { NoCleanup = true };
                    break;

                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'. Try --help.");
            }
        }

        if (adminUser is not null && adminPassword is not null)
        {
            options = options with { Admin = new AdminCredentials(adminUser, adminPassword) };
        }
        else if (adminUser is not null || adminPassword is not null)
        {
            throw new ArgumentException("--admin-user and --admin-password go together.");
        }

        if (options.Server is null && !options.Hosted)
        {
            throw new ArgumentException("Pass --server <url> or --hosted.");
        }

        return options;
    }

    private static string Next(IReadOnlyList<string> args, ref int index, string flag)
    {
        index++;

        if (index >= args.Count)
        {
            throw new ArgumentException($"{flag} needs a value.");
        }

        return args[index];
    }

    public const string Usage = """
        DikuWeb playtesting apparatus — logs characters in, plays a plan, records what happened.

          --server <url>          Play against a running server (the usual case)
          --hosted                Boot a server in-process against a throwaway database
          --plans <path>          A plan file, or a directory of them
          --out <dir>             Where run directories go (default: runs)
          --admin-user <name>     An existing admin, for plans that need an elevated role
          --admin-password <pw>
          --no-fixtures           Play the world as it stands, ignoring plans' world: blocks
                                  (by default, a plan's content is checked and built if missing —
                                   which needs the admin credential above)
          --follow                Print the transcript as it happens
          --no-cleanup            Leave the characters this run created in the world

        Examples:
          dotnet run --project tools/DikuWeb.Playtest -- --server http://localhost:5010
          dotnet run --project tools/DikuWeb.Playtest -- --hosted --plans plans/combat
        """;
}
