namespace RescueSriLanka.Api.DTOs;

/// <summary>
/// Sri Lankan phone numbers: ten digits starting with 0, such as 0771234567. Spaces, dashes and brackets
/// are ignored, and +94 followed by nine digits is accepted. Stored in the ten-digit form.
/// </summary>
public static class PhoneNumbers
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();
        if (text.Any(ch => !(ch is >= '0' and <= '9' || ch is '+' or ' ' or '-' or '(' or ')'))) return null;
        if (text.IndexOf('+') > 0) return null;

        var digits = new string(text.Where(ch => ch is >= '0' and <= '9').ToArray());
        if (text.StartsWith('+') && digits.Length == 11 && digits.StartsWith("94")) return "0" + digits[2..];
        if (digits.Length == 10 && digits[0] == '0' && !text.StartsWith('+')) return digits;
        return null;
    }
}
