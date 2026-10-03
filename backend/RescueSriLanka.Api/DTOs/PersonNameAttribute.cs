using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace RescueSriLanka.Api.DTOs;

/// <summary>A person's or organisation's name: letters, spaces, apostrophes, dots and hyphens. No digits or symbols.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PersonNameAttribute : ValidationAttribute
{
    private static readonly Regex Pattern = new(@"^[\p{L}\p{M}][\p{L}\p{M} .'\-]{0,148}[\p{L}\p{M}.]$", RegexOptions.Compiled);

    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || Pattern.IsMatch(text.Trim())));

    public override string FormatErrorMessage(string name) =>
        $"{name} can contain letters, spaces, apostrophes, dots and hyphens only.";
}
