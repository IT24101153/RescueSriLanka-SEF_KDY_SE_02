using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.DTOs
{
    // Enums here are sent as numbers (the React and Flutter help-request clients
    // read them that way), overriding the API-wide JsonStringEnumConverter.

    // For creating an advisory (coordinator-facing, or later auto-generated from an Incident)
    public class CreateTravelAdvisoryDto
    {
        public string AreaName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }
        [JsonConverter(typeof(JsonNumberEnumConverter<SafetyLevel>))]
        public SafetyLevel SafetyLevel { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
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
        public DateTime? ExpiresAt { get; set; }
    }

    // A single point to check — used for both a single location and a multi-point route
    public class GeoPointDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    // Request: either one point (area check) or several (route check)
    public class SafetyCheckRequestDto
    {
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