namespace RescueSriLanka.Api.Models;

/// <summary>
/// The 25 administrative districts of Sri Lanka, in the order the Department of
/// Census and Statistics lists them (by province).
///
/// Incidents carry a free-text district typed by whoever filed the report, so
/// "colombo", "COLOMBO" and "Colombo" all reach us. A warning email is only as
/// good as the match between that text and the district a citizen picked in
/// their settings, hence <see cref="Normalise"/>: both sides are reduced to one
/// canonical spelling before they are ever compared.
/// </summary>
public static class SriLankaDistricts
{
    public static readonly IReadOnlyList<string> All =
    [
        // Western
        "Colombo", "Gampaha", "Kalutara",
        // Central
        "Kandy", "Matale", "Nuwara Eliya",
        // Southern
        "Galle", "Matara", "Hambantota",
        // Northern
        "Jaffna", "Kilinochchi", "Mannar", "Vavuniya", "Mullaitivu",
        // Eastern
        "Batticaloa", "Ampara", "Trincomalee",
        // North Western
        "Kurunegala", "Puttalam",
        // North Central
        "Anuradhapura", "Polonnaruwa",
        // Uva
        "Badulla", "Monaragala",
        // Sabaragamuwa
        "Ratnapura", "Kegalle"
    ];

    /// <summary>
    /// Canonical spelling for a district name, or null when the text names no
    /// district we know. Tolerates case, surrounding whitespace, a trailing
    /// "District", and the hyphen people put in "Nuwara-Eliya".
    /// </summary>
    public static string? Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var cleaned = value.Trim();

        if (cleaned.EndsWith(" district", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^" district".Length].TrimEnd();
        }

        cleaned = cleaned.Replace('-', ' ').Replace('_', ' ');

        // Collapse repeated inner whitespace so "Nuwara   Eliya" still matches.
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return All.FirstOrDefault(
            district => string.Equals(district, cleaned, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsKnown(string? value) => Normalise(value) is not null;
}
