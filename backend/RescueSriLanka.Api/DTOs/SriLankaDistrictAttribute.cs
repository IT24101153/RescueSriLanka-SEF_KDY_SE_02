using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs;

/// <summary>One of the 25 districts, in any case, as the district lists show them.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SriLankaDistrictAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is null || (value is string text && (string.IsNullOrWhiteSpace(text) || SriLankaDistricts.IsKnown(text)));

    public override string FormatErrorMessage(string name) => $"{name} must be one of the districts of Sri Lanka.";
}
