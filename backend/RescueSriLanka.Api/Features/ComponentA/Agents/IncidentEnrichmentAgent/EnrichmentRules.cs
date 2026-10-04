using System.Globalization;
using System.Text.RegularExpressions;
using RescueSriLanka.Api.Features.ComponentA.Models;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.IncidentEnrichmentAgent;

/// <summary>
/// Deterministic enrichment. Two jobs: the tools the Evidence agent calls
/// (people extraction, text similarity), and the floor the Reasoning agent
/// falls back to when the model is unconfigured or unusable — so a report is
/// still checked for duplicates and a missing district with no model at all.
///
/// The rules are conservative on purpose: they only propose what the data
/// plainly supports, and leave judgement calls (rewording a title) to the model.
/// </summary>
public static partial class EnrichmentRules
{
    /// <summary>Average household size, rounded up — Sri Lanka's census puts it near 3.8.</summary>
    public const int PeoplePerHousehold = 4;

    /// <summary>How far the reported district's main town may sit beyond the nearest one before it looks wrong.</summary>
    public const double DistrictToleranceKm = 25;

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

    private static readonly (IncidentType Type, string[] Words)[] TypeKeywords =
    [
        (IncidentType.Tsunami, ["tsunami", "tidal wave"]),
        (IncidentType.Landslide, ["landslide", "mudslide", "earth slip", "earthslip", "slope collapse", "rockfall"]),
        (IncidentType.Flood, ["flood", "flooding", "inundat", "submerged", "water level", "overflow"]),
        (IncidentType.Fire, ["fire", "smoke", "blaze", "burning", "flames"]),
        (IncidentType.Accident, ["collision", "crash", "accident", "overturned", "derail"]),
        (IncidentType.Storm, ["storm", "cyclone", "gale", "strong wind", "lightning", "tornado"])
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

    /// <summary>The type the text most plainly describes, or null when no keyword appears.</summary>
    public static IncidentType? DetectType(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var (type, words) in TypeKeywords)
        {
            if (words.Any(lower.Contains)) return type;
        }

        return null;
    }

    /// <summary>
    /// The candidate most likely to be the same event, or null. Same hazard,
    /// close together, and either very close or clearly describing the same thing.
    /// </summary>
    public static DuplicateCandidate? BestDuplicate(EnrichmentInput input, IReadOnlyList<DuplicateCandidate> candidates) =>
        candidates
            .Where(candidate => candidate.Type == input.Type && candidate.HoursApart <= 48)
            .Where(candidate =>
                candidate.DistanceKm <= 0.3 ||
                (candidate.DistanceKm <= 1.5 && candidate.TextSimilarity >= 0.2))
            .OrderBy(candidate => candidate.DistanceKm)
            .ThenByDescending(candidate => candidate.TextSimilarity)
            .FirstOrDefault();

    /// <summary>The deterministic floor, used when the model's answer is unusable.</summary>
    public static EnrichmentResult Propose(EnrichmentInput input, EnrichmentEvidence evidence)
    {
        var suggestions = new List<FieldSuggestion>();

        if (evidence.NormalisedDistrict is null)
        {
            suggestions.Add(new FieldSuggestion
            {
                Field = EnrichmentFields.District,
                Current = input.District,
                Proposed = evidence.NearestDistrict,
                Reason = (input.District is null ? "No district was given" : $"'{input.District}' is not a Sri Lankan district")
                         + $"; the pin is {evidence.NearestDistrictKm:F0} km from {evidence.NearestDistrict} town."
            });
        }
        else if (evidence.ReportedDistrictKm is double reportedKm &&
                 reportedKm > evidence.NearestDistrictKm + DistrictToleranceKm &&
                 evidence.NearestDistrict != evidence.NormalisedDistrict)
        {
            suggestions.Add(new FieldSuggestion
            {
                Field = EnrichmentFields.District,
                Current = input.District,
                Proposed = evidence.NearestDistrict,
                Reason = $"The pin is {reportedKm:F0} km from {evidence.NormalisedDistrict} town but only "
                         + $"{evidence.NearestDistrictKm:F0} km from {evidence.NearestDistrict} town."
            });
        }

        if (input.EstimatedAffectedPeople is null && evidence.PeopleFromText is int people)
        {
            suggestions.Add(new FieldSuggestion
            {
                Field = EnrichmentFields.People,
                Current = null,
                Proposed = people.ToString(CultureInfo.InvariantCulture),
                Reason = $"The description says \"{evidence.PeopleEvidence}\""
                         + (evidence.PeopleEvidence is { } e && !e.Contains("people", StringComparison.OrdinalIgnoreCase)
                             ? $" (counted at {PeoplePerHousehold} people per household)." : ".")
            });
        }

        if (input.Type == IncidentType.Other && DetectType($"{input.Title} {input.Description}") is IncidentType detected)
        {
            suggestions.Add(new FieldSuggestion
            {
                Field = EnrichmentFields.Type,
                Current = input.Type.ToString(),
                Proposed = detected.ToString(),
                Reason = $"Filed as Other, but the report describes a {detected.ToString().ToLowerInvariant()}."
            });
        }

        DuplicateProposal? duplicate = null;
        if (BestDuplicate(input, evidence.Candidates) is { } match)
        {
            duplicate = new DuplicateProposal
            {
                IncidentId = match.IncidentId,
                Title = match.Title,
                DistanceKm = match.DistanceKm,
                Similarity = match.TextSimilarity,
                Reason = $"Same hazard ({match.Type}) {match.DistanceKm:F2} km away, reported "
                         + $"{match.HoursApart:F0} h apart, {match.TextSimilarity:P0} word overlap."
            };
        }

        var summary = suggestions.Count == 0 && duplicate is null
            ? "Rule-based check found nothing to correct and no duplicate."
            : $"Rule-based check: {suggestions.Count} field correction(s)"
              + (duplicate is null ? "." : ", and a likely duplicate.");

        return new EnrichmentResult
        {
            Suggestions = suggestions,
            Duplicate = duplicate,
            Summary = summary,
            UsedFallback = true
        };
    }
}
