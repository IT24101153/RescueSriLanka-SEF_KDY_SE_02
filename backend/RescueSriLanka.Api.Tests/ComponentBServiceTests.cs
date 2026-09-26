using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Tests;

public class ComponentBServiceTests
{
    [Fact]
    public async Task UpdateStatusAsync_AllowsTheDefinedLifecycleAndRecordsHistory()
    {
        await using var context = CreateContext();
        var request = new HelpRequest { CitizenId = Guid.NewGuid(), Type = HelpRequestType.Medical };
        context.HelpRequests.Add(request);
        await context.SaveChangesAsync();
        var service = new HelpRequestService(context);
        var coordinatorId = Guid.NewGuid();

        await service.UpdateStatusAsync(request.Id, coordinatorId,
            new UpdateHelpRequestStatusDto { NewStatus = HelpRequestStatus.Assigned });
        await service.UpdateStatusAsync(request.Id, coordinatorId,
            new UpdateHelpRequestStatusDto { NewStatus = HelpRequestStatus.InProgress });
        var result = await service.UpdateStatusAsync(request.Id, coordinatorId,
            new UpdateHelpRequestStatusDto { NewStatus = HelpRequestStatus.Resolved });

        Assert.Equal(HelpRequestStatus.Resolved, result!.Status);
        Assert.Equal(3, await context.RequestStatusHistories.CountAsync());
    }

    [Fact]
    public async Task UpdateStatusAsync_RejectsInvalidOrTerminalTransitions()
    {
        await using var context = CreateContext();
        var request = new HelpRequest { CitizenId = Guid.NewGuid(), Type = HelpRequestType.Food };
        context.HelpRequests.Add(request);
        await context.SaveChangesAsync();
        var service = new HelpRequestService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateStatusAsync(
            request.Id,
            Guid.NewGuid(),
            new UpdateHelpRequestStatusDto { NewStatus = HelpRequestStatus.Resolved }));
    }

    [Fact]
    public async Task VerifyAsync_RejectingAFakeRequestCancelsAndAuditsIt()
    {
        await using var context = CreateContext();
        var request = new HelpRequest
        {
            CitizenId = Guid.NewGuid(),
            Type = HelpRequestType.Rescue,
            Status = HelpRequestStatus.Assigned
        };
        context.HelpRequests.Add(request);
        await context.SaveChangesAsync();
        var service = new HelpRequestService(context);
        var verifierId = Guid.NewGuid();

        var result = await service.VerifyAsync(request.Id, verifierId,
            new VerifyHelpRequestDto { IsReal = false, Notes = "Duplicate report" });
        var history = await context.RequestStatusHistories.SingleAsync();

        Assert.Equal(VerificationStatus.RejectedFake, result!.VerificationStatus);
        Assert.Equal(HelpRequestStatus.Cancelled, result.Status);
        Assert.Equal(verifierId, history.ChangedByUserId);
        Assert.Equal(HelpRequestStatus.Assigned, history.OldStatus);
        Assert.Equal(HelpRequestStatus.Cancelled, history.NewStatus);
    }

    [Fact]
    public async Task CheckSafetyAsync_ReturnsTheWorstMatchingAdvisory()
    {
        await using var context = CreateContext();
        context.TravelAdvisories.AddRange(
            new TravelAdvisory
            {
                AreaName = "Caution area", Latitude = 6.9271, Longitude = 79.8612,
                RadiusMeters = 1_000, SafetyLevel = SafetyLevel.Caution, Reason = "Flood risk"
            },
            new TravelAdvisory
            {
                AreaName = "Danger area", Latitude = 6.9271, Longitude = 79.8612,
                RadiusMeters = 500, SafetyLevel = SafetyLevel.Danger, Reason = "Active landslide"
            });
        await context.SaveChangesAsync();
        var service = new TravelAdvisoryService(context);

        var result = await service.CheckSafetyAsync(new SafetyCheckRequestDto
        {
            Points = [new GeoPointDto { Latitude = 6.9271, Longitude = 79.8612 }]
        });

        Assert.Equal(SafetyLevel.Danger, result.OverallSafetyLevel);
        Assert.Equal("Active landslide", result.Reason);
        Assert.Equal(2, result.MatchedAdvisories.Count);
    }

    [Fact]
    public async Task TravelAdvisoryCrudAsync_UpdatesAndDeletesAnAdvisory()
    {
        await using var context = CreateContext();
        var service = new TravelAdvisoryService(context);
        var created = await service.CreateAsync(new CreateTravelAdvisoryDto
        {
            AreaName = "Original area",
            Latitude = 6.9271,
            Longitude = 79.8612,
            RadiusMeters = 500,
            SafetyLevel = SafetyLevel.Caution,
            Reason = "Flood risk"
        });

        var read = await service.GetByIdAsync(created.Id);
        var updated = await service.UpdateAsync(created.Id, new UpdateTravelAdvisoryDto
        {
            AreaName = "Updated area",
            Latitude = 7.2906,
            Longitude = 80.6337,
            RadiusMeters = 750,
            SafetyLevel = SafetyLevel.Danger,
            Reason = "Landslide risk"
        });
        var deleted = await service.DeleteAsync(created.Id);

        Assert.NotNull(read);
        Assert.Equal("Updated area", updated!.AreaName);
        Assert.Equal(SafetyLevel.Danger, updated.SafetyLevel);
        Assert.True(deleted);
        Assert.Null(await service.GetByIdAsync(created.Id));
    }

    [Fact]
    public void AdvisoryValidation_RejectsAnUnknownSafetyLevel()
    {
        var dto = new CreateTravelAdvisoryDto
        {
            AreaName = "Test area",
            Latitude = 6.9271,
            Longitude = 79.8612,
            RadiusMeters = 500,
            SafetyLevel = (SafetyLevel)99,
            Reason = "Test reason",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, true);

        Assert.False(valid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(dto.SafetyLevel)));
    }

    [Fact]
    public void AdvisoryValidation_RejectsAPastExpiry()
    {
        var dto = new CreateTravelAdvisoryDto
        {
            AreaName = "Test area",
            Latitude = 6.9271,
            Longitude = 79.8612,
            RadiusMeters = 500,
            SafetyLevel = SafetyLevel.Caution,
            Reason = "Test reason",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, true);

        Assert.False(valid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(dto.ExpiresAt)));
    }

    [Fact]
    public async Task CreateHelpRequestAsync_RejectsAnUnknownRelatedIncident()
    {
        await using var context = CreateContext();
        var service = new HelpRequestService(context);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            Guid.NewGuid(),
            new CreateHelpRequestDto
            {
                Type = HelpRequestType.Water,
                Description = "Water is needed at this location.",
                Latitude = 6.9271,
                Longitude = 79.8612,
                RelatedIncidentId = Guid.NewGuid()
            }));

        Assert.Contains("related incident", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateHelpRequestAsync_AllowsTheRequesterToEditOnlyAPendingRequest()
    {
        await using var context = CreateContext();
        var citizenId = Guid.NewGuid();
        var request = new HelpRequest
        {
            CitizenId = citizenId,
            Type = HelpRequestType.Food,
            Description = "Food needed.",
            Latitude = 6.9271,
            Longitude = 79.8612
        };
        context.HelpRequests.Add(request);
        await context.SaveChangesAsync();
        var service = new HelpRequestService(context);

        var updated = await service.UpdateAsync(request.Id, citizenId,
            new UpdateHelpRequestDto
            {
                Type = HelpRequestType.Medical,
                Description = "Medical assistance is urgently needed.",
                Latitude = 7.2906,
                Longitude = 80.6337
            });

        Assert.Equal(HelpRequestType.Medical, updated!.Type);
        Assert.Equal(80, updated.UrgencyScore);

        await service.UpdateStatusAsync(request.Id, Guid.NewGuid(),
            new UpdateHelpRequestStatusDto { NewStatus = HelpRequestStatus.Assigned });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(request.Id, citizenId,
            new UpdateHelpRequestDto
            {
                Type = HelpRequestType.Water,
                Description = "Water needed.",
                Latitude = 7.2906,
                Longitude = 80.6337
            }));
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
