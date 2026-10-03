using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.DTOs;

/// <summary>A Sri Lankan phone number. An empty value is left to [Required] to judge.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SriLankaPhoneAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is string text && (string.IsNullOrWhiteSpace(text) || PhoneNumbers.Normalize(text) is not null));

    public override string FormatErrorMessage(string name) =>
        $"{name} must be a 10-digit Sri Lankan phone number, such as 0771234567.";
}
