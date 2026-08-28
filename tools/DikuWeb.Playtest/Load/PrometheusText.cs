using System.Globalization;

namespace DikuWeb.Playtest.Load;

/// <summary>One line of the exposition: a metric name, its labels, and its value.</summary>
public sealed record PrometheusSample(
    string Name,
    IReadOnlyDictionary<string, string> Labels,
    double Value);

/// <summary>
/// Reads the Prometheus text exposition that <c>/metrics</c> serves.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled, for the same reason <see cref="Options"/> is: the surface actually used here is
/// four metrics on one endpoint this repo also writes, and a scraping library would be a
/// dependency carried for a parser that fits on a screen.
/// </para>
/// <para>
/// <b>Label values are parsed, never matched as strings.</b> The exporter writes bucket boundaries
/// through a round-trip double format, so the 0.1 boundary is emitted as
/// <c>le="0.10000000000000001"</c> — a parser comparing <c>le</c> to the literal "0.1" finds no
/// such bucket and silently reports an empty histogram. That is the one mistake here that produces
/// a plausible answer rather than an error.
/// </para>
/// </remarks>
public static class PrometheusText
{
    public static IReadOnlyList<PrometheusSample> Parse(string exposition)
    {
        ArgumentNullException.ThrowIfNull(exposition);

        var samples = new List<PrometheusSample>();

        foreach (var raw in exposition.Split('\n'))
        {
            var line = raw.Trim();

            // # HELP and # TYPE carry nothing this needs, and a blank line ends a metric family.
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (ParseLine(line) is { } sample)
            {
                samples.Add(sample);
            }
        }

        return samples;
    }

    private static PrometheusSample? ParseLine(string line)
    {
        var brace = line.IndexOf('{', StringComparison.Ordinal);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        string name;
        string remainder;

        if (brace < 0)
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);

            if (space < 0)
            {
                return null;
            }

            name = line[..space];
            remainder = line[(space + 1)..];
        }
        else
        {
            var close = line.LastIndexOf('}');

            if (close < brace)
            {
                return null;
            }

            name = line[..brace];
            ParseLabels(line[(brace + 1)..close], labels);
            remainder = line[(close + 1)..].TrimStart();
        }

        // A sample may carry a trailing millisecond timestamp; the value is the first field.
        var value = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var first, ..]
            ? first
            : remainder;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? new PrometheusSample(name, labels, parsed)
            : null;
    }

    /// <summary>
    /// Splits <c>a="1",b="2"</c> into pairs.
    /// </summary>
    /// <remarks>
    /// Escapes are honoured because the exposition format allows them in label values, and a
    /// description containing a comma would otherwise split one label into two.
    /// </remarks>
    private static void ParseLabels(string block, Dictionary<string, string> into)
    {
        var index = 0;

        while (index < block.Length)
        {
            var equals = block.IndexOf('=', index);

            if (equals < 0)
            {
                return;
            }

            var key = block[index..equals].Trim();
            var open = block.IndexOf('"', equals);

            if (open < 0)
            {
                return;
            }

            var value = new System.Text.StringBuilder();
            var cursor = open + 1;

            while (cursor < block.Length && block[cursor] != '"')
            {
                if (block[cursor] == '\\' && cursor + 1 < block.Length)
                {
                    cursor++;

                    value.Append(block[cursor] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        var other => other,
                    });
                }
                else
                {
                    value.Append(block[cursor]);
                }

                cursor++;
            }

            into[key] = value.ToString();

            // Past the closing quote and the comma that follows it.
            index = cursor + 1;

            while (index < block.Length && (block[index] == ',' || block[index] == ' '))
            {
                index++;
            }
        }
    }
}
