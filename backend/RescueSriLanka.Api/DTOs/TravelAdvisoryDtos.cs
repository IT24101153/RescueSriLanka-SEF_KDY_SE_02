using System;
using System.Collections.Generic;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.DTOs
{
    // For creating an advisory (coordinator-facing, or later auto-generated from an Incident)
    public class CreateTravelAdvisoryDto
    {
        public string AreaName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }
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
        public List<GeoPointDto> Points { get; set; } = new();
    }

    // Response: the worst safety level found across all points/advisories, plus why
    public class SafetyCheckResponseDto
    {
        public SafetyLevel OverallSafetyLevel { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<TravelAdvisoryResponseDto> MatchedAdvisories { get; set; } = new();
    }
}