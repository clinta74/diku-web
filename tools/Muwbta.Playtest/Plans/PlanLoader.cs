using Muwbta.Playtest.Session;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Muwbta.Playtest.Plans;

/// <summary>Reads plan files off disk.</summary>
public static class PlanLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        // Hyphenated so `expect-not` reads as English rather than as an identifier. The properties
        // it maps to are ordinary PascalCase.
        .WithNamingConvention(HyphenatedNamingConvention.Instance)

        // A key the model does not have is a typo in a plan, and a typo that is silently ignored
        // is a plan that quietly stops testing what its author thought. Better to refuse it and
        // say which key.
        .WithDuplicateKeyChecking()
        .Build();

    /// <summary>
    /// Every plan under a path, in a stable order.
    /// </summary>
    /// <param name="path">A single <c>.yaml</c> file, or a directory searched recursively.</param>
    public static IReadOnlyList<PlanDocument> LoadAll(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (File.Exists(path))
        {
            return [Load(path)];
        }

        if (!Directory.Exists(path))
        {
            throw new PlaytestException($"No plan file or directory at '{path}'.");
        }

        var files = Directory
            .EnumerateFiles(path, "*.yaml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(path, "*.yml", SearchOption.AllDirectories))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            throw new PlaytestException($"No .yaml plans under '{path}'.");
        }

        return [.. files.Select(Load)];
    }

    public static PlanDocument Load(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        string text;

        try
        {
            text = File.ReadAllText(file);
        }
        catch (IOException ex)
        {
            throw new PlaytestException($"Could not read plan '{file}': {ex.Message}", ex);
        }

        return Parse(text, file);
    }

    /// <summary>Parses plan text, naming the source in any failure.</summary>
    public static PlanDocument Parse(string text, string? source = null)
    {
        PlanDocument? plan;

        try
        {
            plan = Yaml.Deserialize<PlanDocument>(text);
        }
        catch (YamlException ex)
        {
            throw new PlaytestException(
                $"'{source ?? "plan"}' is not valid: {ex.Message} (line {ex.Start.Line}).", ex);
        }

        if (plan is null)
        {
            throw new PlaytestException($"'{source ?? "plan"}' is empty.");
        }

        plan.SourcePath = source;
        Validate(plan, source);
        return plan;
    }

    /// <summary>
    /// Catches the mistakes that would otherwise surface as a confusing transcript.
    /// </summary>
    /// <remarks>
    /// All of these are things a plan author gets wrong at some point, and every one of them
    /// produces output that looks like a game bug rather than a plan bug — a step naming an actor
    /// who is not in the cast simply never runs, which reads as the game ignoring a command.
    /// </remarks>
    private static void Validate(PlanDocument plan, string? source)
    {
        var where = source ?? plan.Name;

        if (plan.Cast.Count == 0)
        {
            throw new PlaytestException($"'{where}' has no cast; a plan needs at least one actor.");
        }

        var names = plan.Cast.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (names.Count != plan.Cast.Count)
        {
            throw new PlaytestException($"'{where}' has two cast members with the same name.");
        }

        foreach (var step in Flatten(plan.Steps))
        {
            if (step.Actor is null)
            {
                // A step with no actor must be a note, or there is nothing for it to do.
                if (step.Note is null && step.Together.Count == 0)
                {
                    throw new PlaytestException(
                        $"'{where}' has a step with no actor and nothing to say.");
                }

                continue;
            }

            if (!names.Contains(step.Actor))
            {
                throw new PlaytestException(
                    $"'{where}' has a step for '{step.Actor}', who is not in the cast. " +
                    $"The cast is: {string.Join(", ", names)}.");
            }
        }
    }

    private static IEnumerable<PlanStep> Flatten(IEnumerable<PlanStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;

            foreach (var nested in Flatten(step.Together))
            {
                yield return nested;
            }
        }
    }
}
