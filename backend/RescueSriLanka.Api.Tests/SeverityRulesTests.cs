using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
using RescueSriLanka.Api.Features.ComponentA.Models;


namespace RescueSriLanka.Api.Tests;

/// <summary>
/// The Incident Analysis Agent's deterministic floor.
///
/// This is what runs when the language model is unconfigured, unreachable,
/// rate limited, or returns something that fails validation — so these tests
/// stand for "an incident is never left unscored". They are also the part of
/// the agent that is fully explainable, which is exactly what a coordinator
/// asks about when a report is graded Critical.
/// </summary>
public class SeverityRulesTests
{
    private static IncidentAnalysisInput NewInput(
        IncidentType type = IncidentType.Flood,
        int? people = null) => new()
        {
            IncidentId = Guid.NewGuid(),
            Title = "Test incident",
            Description = "Created by a unit test.",
            Type = type,
            Latitude = 6.9271,
            Longitude = 79.8612,
            District = "Colombo",
            EstimatedAffectedPeople = people
        };

    [Fact]
    public void Score_AlwaysMarksItselfAsAFallback()
    {
        // The audit trail depends on this: a rule-engine result must never be
        // presentable as a model's judgement.
        Assert.True(SeverityRules.Score(NewInput(), nearbyIncidents: 0).UsedFallback);
    }

    [Fact]
    public void Score_StaysWithinZeroToOneHundred()
    {
        // Worst case on every axis at once must still land in range.
        var result = SeverityRules.Score(
            NewInput(IncidentType.Tsunami, people: 100000),
            nearbyIncidents: 50,
            rainfallMm48h: 900);

        Assert.InRange(result.SeverityScore, 0, 100);
    }

    [Fact]
    public void Score_RatesATsunamiAboveARoadAccident()
    {
        var tsunami = SeverityRules.Score(NewInput(IncidentType.Tsunami), 0);
        var accident = SeverityRules.Score(NewInput(IncidentType.Accident), 0);

        Assert.True(tsunami.SeverityScore > accident.SeverityScore);
    }

    [Fact]
    public void Score_RisesWithThePeopleAtRisk()
    {
        var few = SeverityRules.Score(NewInput(people: 5), 0);
        var many = SeverityRules.Score(NewInput(people: 5000), 0);

        Assert.True(many.SeverityScore > few.SeverityScore);
    }

    [Fact]
    public void Score_RisesWithClusteredReports()
    {
        // Several reports in one area mean a bigger event than any single one
        // of them describes.
        var isolated = SeverityRules.Score(NewInput(), nearbyIncidents: 0);
        var clustered = SeverityRules.Score(NewInput(), nearbyIncidents: 6);

        Assert.True(clustered.SeverityScore > isolated.SeverityScore);
    }

    [Fact]
    public void Score_CountsRainfallForFloodsAndLandslides()
    {
        var dry = SeverityRules.Score(NewInput(IncidentType.Flood), 0, rainfallMm48h: 0);
        var wet = SeverityRules.Score(NewInput(IncidentType.Flood), 0, rainfallMm48h: 250);

        Assert.True(wet.SeverityScore > dry.SeverityScore);
    }

    [Fact]
    public void Score_IgnoresRainfallForAFire()
    {
        // Rain is the driver behind Sri Lankan floods and landslides. It has no
        // business raising the score of a road accident or a fire.
        var dry = SeverityRules.Score(NewInput(IncidentType.Fire), 0, rainfallMm48h: 0);
        var wet = SeverityRules.Score(NewInput(IncidentType.Fire), 0, rainfallMm48h: 250);

        Assert.Equal(dry.SeverityScore, wet.SeverityScore);
    }

    [Fact]
    public void Score_PairsAHighSeverityWithADangerZone()
    {
        var result = SeverityRules.Score(
            NewInput(IncidentType.Tsunami, people: 5000), nearbyIncidents: 5);

        Assert.True(result.Severity is IncidentSeverity.High or IncidentSeverity.Critical);
        Assert.Equal(ZoneStatus.Danger, result.RecommendedZoneStatus);
    }

    [Fact]
    public void Score_GivesASmallIncidentASmallerRadius()
    {
        var minor = SeverityRules.Score(NewInput(IncidentType.Accident), 0);
        var major = SeverityRules.Score(
            NewInput(IncidentType.Tsunami, people: 5000), nearbyIncidents: 5);

        Assert.True(major.RecommendedRadiusMeters > minor.RecommendedRadiusMeters);
    }

    [Fact]
    public void Score_ExplainsItself()
    {
        var result = SeverityRules.Score(NewInput(people: 300), nearbyIncidents: 2);

        // A coordinator has to be able to read why, not just what.
        Assert.Contains(result.SeverityScore.ToString(), result.Rationale);
        Assert.Contains("300", result.Rationale);
        Assert.Contains("Flood", result.Rationale);
    }

    [Fact]
    public void Score_NeverClaimsModelLevelConfidence()
    {
        // Rules are transparent but blunt; overstating certainty here would
        // mislead the person approving the proposal.
        Assert.InRange(SeverityRules.Score(NewInput(), 0).Confidence, 0.0, 0.8);
    }
}
