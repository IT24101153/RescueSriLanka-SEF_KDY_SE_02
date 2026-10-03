using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.DTOs;

/// <summary>An absolute http or https address. Other schemes (javascript:, data:, file:) are refused.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class HttpUrlAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is string text
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    public override string FormatErrorMessage(string name) => $"{name} must be an http or https address.";
}
