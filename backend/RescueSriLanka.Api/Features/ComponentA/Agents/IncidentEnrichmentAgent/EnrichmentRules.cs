using System.Globalization;
using System.Text.RegularExpressions;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.IncidentEnrichmentAgent;

/// <summary>
/// The deterministic tools the Evidence agent calls: people extraction and
/// text similarity. They measure; they never propose. Every proposal comes
/// from the language model, which is shown what these found.
/// </summary>
public static partial class EnrichmentRules
{
    /// <summary>Average household size, rounded up — Sri Lanka's census puts it near 3.8.</summary>
    public const int PeoplePerHousehold = 4;

    // "40 families", "1,200 people", "about 15 houses" — the number and its noun.
    [GeneratedRegex(
        @"(\d{1,3}(?:,\d{3})+|\d+)\s+(?:\w+\s+)?(people|persons|residents|villagers|individuals|families|households|houses|homes)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex PeoplePattern();

    [GeneratedRegex(@"[a-z]{3,}")]
    private static partial Regex WordPattern();

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "near", "are", "was", "were", "has", "have", "with", "from", "for",
        "this", "that", "there", "into", "onto", "our", "their", "they", "them", "not",
        "very", "some", "many", "more", "now", "still", "road", "area", "town", "village"
    ];

    /// <summary>
    /// The largest head-count stated in the text, households converted to
    /// people. Null when the text gives no number.
    /// </summary>
    public static (int People, string Evidence)? ExtractPeople(string text)
    {
        (int People, string Evidence)? best = null;

        foreach (Match match in PeoplePattern().Matches(text))
        {
            if (!int.TryParse(match.Groups[1].Value.Replace(",", ""), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var count))
            {
                continue;
            }

            var noun = match.Groups[2].Value.ToLowerInvariant();
            var perUnit = noun is "families" or "households" or "houses" or "homes" ? PeoplePerHousehold : 1;
            var people = (int)Math.Min((long)count * perUnit, 1_000_000);

            if (best is null || people > best.Value.People)
            {
                best = (people, match.Value.Trim());
            }
        }

        return best;
    }

    /// <summary>Jaccard overlap of the meaningful words in two texts, 0-1.</summary>
    public static double TextSimilarity(string a, string b)
    {
        var left = Words(a);
        var right = Words(b);
        if (left.Count == 0 || right.Count == 0) return 0;

        var shared = left.Intersect(right).Count();
        return Math.Round((double)shared / left.Union(right).Count(), 3);
    }

    private static HashSet<string> Words(string text) =>
        [.. WordPattern().Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(word => !StopWords.Contains(word))];
}
