using System.Globalization;
using System.Text.RegularExpressions;
using RescueSriLanka.Api.Features.ComponentC.Models;

namespace RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;

/// <summary>
/// The allocation agent's deterministic floor, used whenever the AI cannot answer (quota used up,
/// service down, no key). It only decides what it can read off the request: the item and the amount
/// the requester asked for. A submission is stored as "Item name - quantity unit", for example
/// "Bandages - 20 packs". With no amount stated it declines, because any number would be invented.
/// The manager still approves every result.
/// </summary>
internal static class ResourceAllocationRules
{
    public const string Source = "Rules";

    private static readonly Regex Structured = new(
        @"^\s*(?<item>.+?)\s+-\s+(?<qty>\d+(?:\.\d+)?)\s*(?<unit>[^\W\d_][\w/ ]*)?\s*$", RegexOptions.Compiled);
    private static readonly Regex AnyNumber = new(
        @"(?<qty>\d+(?:\.\d+)?)\s*(?<unit>[^\W\d_]+)?", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"[a-z]+", RegexOptions.Compiled);

    // Words that say nothing about which item is wanted.
    private static readonly HashSet<string> Ignored =
    [
        "need", "needs", "needed", "please", "request", "send", "with", "for", "and", "the", "some",
        "pack", "packs", "box", "boxes", "unit", "units", "item", "items", "family", "families", "urgent"
    ];

    /// <summary>What a request asks for, as far as it can be read.</summary>
    internal sealed record Need(string Text, decimal? Quantity, string? Unit);

    public static Need Parse(HelpRequest request)
    {
        var structured = Structured.Match(request.Description);
        if (structured.Success)
        {
            return new Need(
                structured.Groups["item"].Value.Trim(),
                ParseNumber(structured.Groups["qty"].Value),
                NullIfEmpty(structured.Groups["unit"].Value));
        }

        var number = AnyNumber.Match(request.Description);
        return new Need(
            $"{request.NeedType} {request.Description}",
            number.Success ? ParseNumber(number.Groups["qty"].Value) : null,
            number.Success ? NullIfEmpty(number.Groups["unit"].Value) : null);
    }

    // Medical first, then water and food, then the rest; within a group the older request goes first.
    public static int Urgency(HelpRequest request)
    {
        var need = request.NeedType.ToLowerInvariant();
        if (need.Contains("medic")) return 30;
        if (need.Contains("water") || need.Contains("food")) return 20;
        if (need.Contains("sanitary") || need.Contains("hygiene")) return 10;
        return 0;
    }

    /// <param name="remaining">Stock still unclaimed by earlier requests in a plan; null means everything on hand.</param>
    public static ResourceAllocationRecommendation Recommend(
        HelpRequest request,
        IReadOnlyList<ResourceAllocationCandidate> candidates,
        IReadOnlyDictionary<Guid, decimal>? remaining = null)
    {
        var need = Parse(request);
        if (need.Quantity is null or <= 0)
        {
            return NoMatch("The request does not state a quantity, so no amount can be suggested. Review it manually.");
        }

        var wanted = Tokens(need.Text);
        var matches = candidates
            .Select(candidate => (
                Candidate: candidate,
                Left: remaining?.GetValueOrDefault(candidate.Id) ?? candidate.QuantityOnHand,
                Name: NameScore(candidate, need.Text, wanted)))
            .Where(match => match.Name > 0)
            .ToList();
        if (matches.Count == 0)
        {
            return NoMatch($"No available stock matches \"{need.Text}\". Review it manually.");
        }

        var usable = matches.Where(match => match.Left > 0).ToList();
        if (usable.Count == 0)
        {
            return NoMatch("The matching stock is already allocated to higher-priority requests.");
        }

        var best = usable
            .OrderByDescending(match => match.Name + CategoryBonus(request, match.Candidate))
            .ThenByDescending(match => UnitsAgree(need.Unit, match.Candidate.Unit))
            .ThenByDescending(match => match.Left)
            .ThenBy(match => match.Candidate.Name, StringComparer.OrdinalIgnoreCase)
            .First();

        var requested = need.Quantity.Value;
        var quantity = Math.Min(requested, best.Left);
        var warnings = new List<string>();
        var confidence = best.Name >= 3 ? 0.85m : 0.6m;

        if (quantity < requested)
        {
            warnings.Add($"Only {Format(best.Left)} {best.Candidate.Unit} available of the {Format(requested)} requested; this covers part of the need.");
            confidence -= 0.1m;
        }

        if (need.Unit is not null && !UnitsAgree(need.Unit, best.Candidate.Unit))
        {
            warnings.Add($"The request is in {need.Unit} but this stock is counted in {best.Candidate.Unit}. Check the amount.");
            confidence -= 0.15m;
        }

        return new ResourceAllocationRecommendation(
            "Recommend",
            best.Candidate.Id,
            best.Candidate.ResourceType,
            quantity,
            Math.Clamp(confidence, 0.2m, 1m),
            $"\"{best.Candidate.Name}\" matches the request for {Format(requested)} {need.Unit ?? "units"}.",
            warnings,
            true,
            best.Candidate.Name,
            best.Candidate.Unit,
            best.Candidate.QuantityOnHand,
            Source);
    }

    private static ResourceAllocationRecommendation NoMatch(string reason) =>
        new("NoMatch", null, null, 0, 0, reason, [], true, Source: Source);

    // Whole-name match counts most, then each word the request and the stock name share.
    private static int NameScore(ResourceAllocationCandidate candidate, string needText, HashSet<string> wanted)
    {
        var score = 0;
        if (string.Equals(candidate.Name.Trim(), needText.Trim(), StringComparison.OrdinalIgnoreCase)) score += 2;
        score += Tokens(candidate.Name).Count(wanted.Contains);
        return score;
    }

    // The need type ("Medical", "Water") points at a kind of stock; this only breaks ties between name matches.
    private static int CategoryBonus(HelpRequest request, ResourceAllocationCandidate candidate)
    {
        var need = request.NeedType.ToLowerInvariant();
        if (string.Equals(candidate.Category, request.NeedType, StringComparison.OrdinalIgnoreCase)) return 1;
        if (need.Contains("medic") && candidate.ResourceType == "MedicalSupply") return 1;
        if ((need.Contains("food") || need.Contains("water")) && candidate.ResourceType == "FoodWaterStock") return 1;
        return 0;
    }

    private static bool UnitsAgree(string? requested, string stocked) =>
        requested is null || NormaliseUnit(requested) == NormaliseUnit(stocked);

    private static string NormaliseUnit(string unit)
    {
        var value = unit.Trim().ToLowerInvariant();
        return value switch
        {
            "l" or "ltr" or "ltrs" or "liter" or "liters" or "litre" or "litres" => "litre",
            "kg" or "kgs" or "kilo" or "kilos" or "kilogram" or "kilograms" => "kg",
            _ => value.EndsWith('s') && value.Length > 3 ? value[..^1] : value
        };
    }

    private static HashSet<string> Tokens(string text) =>
        [.. Word.Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(word => word.Length >= 3 && !Ignored.Contains(word))
            .Select(Stem)];

    // "bandages" and "bandage" should match.
    private static string Stem(string word) =>
        word.EndsWith('s') && word.Length > 3 ? word[..^1] : word;

    private static decimal? ParseNumber(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
