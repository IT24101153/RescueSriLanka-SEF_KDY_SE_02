using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.DTOs
{
    // Enums here are sent as numbers (the React and Flutter help-request clients
    // read them that way), overriding the API-wide JsonStringEnumConverter.

    // For creating an advisory (coordinator-facing, or later auto-generated from an Incident)
    public class CreateTravelAdvisoryDto : IValidatableObject
    {
        [Required, StringLength(200)]
        public string AreaName { get; set; } = string.Empty;
        [Range(-90, 90)]
        public double Latitude { get; set; }
        [Range(-180, 180)]
        public double Longitude { get; set; }
        [Range(1, 100000)]
        public double RadiusMeters { get; set; }
        [JsonConverter(typeof(JsonNumberEnumConverter<SafetyLevel>))]
        [EnumDataType(typeof(SafetyLevel))]
        public SafetyLevel SafetyLevel { get; set; }
        [Required, StringLength(1000)]
        public string Reason { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ExpiresAt is not null && ExpiresAt <= DateTime.UtcNow)
            {
                yield return new ValidationResult(
                    "Expiry time must be in the future.",
                    [nameof(ExpiresAt)]);
            }
        }
    }

    public class UpdateTravelAdvisoryDto : CreateTravelAdvisoryDto
    {
    }

    public class TravelAdvisoryResponseDto
    {
        public Guid Id { get; set; }
        public string AreaName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }
        [JsonConverter(typeof(JsonNumberEnumConverter<SafetyLevel>))]
        public SafetyLevel SafetyLevel { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    // A single point to check — used for both a single location and a multi-point route
    public class GeoPointDto
    {
        [Range(-90, 90)]
        public double Latitude { get; set; }
        [Range(-180, 180)]
        public double Longitude { get; set; }
    }

    // Request: either one point (area check) or several (route check)
    public class SafetyCheckRequestDto
    {
        [Required, MinLength(1), MaxLength(100)]
        public List<GeoPointDto> Points { get; set; } = [];
    }

    // Response: the worst safety level found across all points/advisories, plus why
    public class SafetyCheckResponseDto
    {
        [JsonConverter(typeof(JsonNumberEnumConverter<SafetyLevel>))]
        public SafetyLevel OverallSafetyLevel { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<TravelAdvisoryResponseDto> MatchedAdvisories { get; set; } = [];
    }
}
