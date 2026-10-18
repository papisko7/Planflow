using System.Net;
using System.Text.RegularExpressions;

namespace PlanFlow.Application.Calendar.Common;

/// <summary>What a <c>[planflow]</c> line gave us. A field the line did not provide (or provided out of range) stays null.</summary>
public record PlanflowTag(int? ImpactScore, double? EffortHours);

/// <summary>
/// Deterministic (no AI) reader of the optional <c>[planflow]</c> line a user can put in a Google Calendar title or
/// description to give an imported event the data Google has no field for:
/// <code>[planflow] impact=8 effort=2h</code>
/// Only the rest of that one line is read. <c>impact</c> is 0–10; <c>effort</c> is a number with an optional unit
/// (<c>min</c>, <c>h</c>, <c>d</c>, <c>w</c>, <c>mo</c> — same units as the app, a month is 30 days; no unit = hours).
/// Keys are case-insensitive, <c>=</c> and <c>:</c> both work. Anything unreadable is ignored, never an error.
/// </summary>
public static partial class PlanflowTagParser
{
    private const double MaxEffortHours = 366 * 24; // same upper bound the task validator uses

    [GeneratedRegex(@"\[planflow\][^\r\n]*", RegexOptions.IgnoreCase)]
    private static partial Regex TagLine();

    [GeneratedRegex(@"\bimpact\s*[=:]\s*(\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Impact();

    [GeneratedRegex(@"\beffort\s*[=:]\s*(\d+(?:[.,]\d+)?)\s*(mo|min|m|h|d|w)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Effort();

    [GeneratedRegex(@"<\s*(br|/p|/div|/li)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    /// <summary>
    /// Reads the <c>[planflow]</c> line of each given text (title first, then description); null when none has one.
    /// Each field comes from the first text that provides it, so the title can set impact and the description effort.
    /// </summary>
    public static PlanflowTag? Parse(params string?[] texts)
    {
        var found = false;
        int? impact = null;
        double? effort = null;

        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var line = TagLine().Match(text);
            if (line.Success)
            {
                found = true;
                impact ??= ReadImpact(line.Value);
                effort ??= ReadEffort(line.Value);
            }
        }

        return found ? new PlanflowTag(impact, effort) : null;
    }

    /// <summary>Removes the <c>[planflow]</c> line from a title, so "Sprint review [planflow] impact=8" shows as "Sprint review".</summary>
    public static string StripFromTitle(string title) => TagLine().Replace(title, string.Empty).Trim();

    /// <summary>
    /// Google lets people write rich-text descriptions, which arrive as HTML ("line one&lt;br&gt;[planflow] impact=8").
    /// Turns that into plain text with real line breaks, so the tag line can be found and the app shows readable text.
    /// </summary>
    public static string? ToPlainText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var text = LineBreakTag().Replace(description, "\n");
        text = WebUtility.HtmlDecode(AnyTag().Replace(text, string.Empty)).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Removes the <c>[planflow]</c> line(s) from an already plain-text description. The tag only fills fields, so it is not kept
    /// in the stored text; lines around it stay, and the blank line a removal leaves behind is dropped. Null when nothing is left.
    /// </summary>
    public static string? StripTagLines(string? plainDescription)
    {
        if (string.IsNullOrWhiteSpace(plainDescription))
        {
            return null;
        }

        var kept = plainDescription
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => !TagLine().IsMatch(line))
            .ToList();
        var text = string.Join('\n', kept).Trim();
        return text.Length == 0 ? null : text;
    }

    private static int? ReadImpact(string line)
    {
        var match = Impact().Match(line);
        return match.Success && int.Parse(match.Groups[1].Value) is >= 0 and <= 10
            ? int.Parse(match.Groups[1].Value)
            : null;
    }

    private static double? ReadEffort(string line)
    {
        var match = Effort().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var value = double.Parse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        var hoursPerUnit = match.Groups[2].Value.ToLowerInvariant() switch
        {
            "min" or "m" => 1.0 / 60,
            "d" => 24,
            "w" => 24 * 7,
            "mo" => 24 * 30,
            _ => 1 // "h" or no unit
        };
        var hours = value * hoursPerUnit;

        return hours > 0 && hours <= MaxEffortHours ? hours : null;
    }
}
