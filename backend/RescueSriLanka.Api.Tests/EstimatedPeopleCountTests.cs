using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;
using RescueSriLanka.Api.Features.ComponentD.Services;

namespace RescueSriLanka.Api.Tests;

public class EstimatedPeopleCountTests
{
    [Fact]
    public async Task CitizenSubmissionPersistsAndReturnsCountAndExistingFields()
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client("Citizen");
        var response = await client.PostAsJsonAsync("/api/helprequests", new {
            type = 3, description = "Family needs rescue", latitude = 7, longitude = 80,
            estimatedPeopleCount = 12, imageUrl = "https://example.test/photo.jpg"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<HelpRequestResponseDto>())!;
        Assert.Equal(12, dto.EstimatedPeopleCount);
        Assert.Equal(HelpRequestStatus.Pending, dto.Status);
        Assert.Equal(VerificationStatus.PendingVerification, dto.VerificationStatus);
        Assert.Equal(HelpRequestType.Rescue, dto.Type);
        Assert.Equal("https://example.test/photo.jpg", dto.ImageUrl);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(12, (await db.HelpRequests.AsNoTracking().SingleAsync()).EstimatedPeopleCount);
        Assert.Equal(12, (await client.GetFromJsonAsync<HelpRequestResponseDto>($"/api/helprequests/{dto.Id}"))!.EstimatedPeopleCount);
        Assert.Empty(db.RequestStatusHistories);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"estimatedPeopleCount\":null")]
    [InlineData(",\"estimatedPeopleCount\":0")]
    [InlineData(",\"estimatedPeopleCount\":-1")]
    [InlineData(",\"estimatedPeopleCount\":1.5")]
    public async Task InvalidCitizenCountIsRejectedBeforePersistence(string count)
    {
        using var factory = new ComponentDApiFactory();
        using var client = factory.Client("Citizen");
        using var body = new StringContent("{\"type\":3,\"description\":\"Family needs rescue\",\"latitude\":7,\"longitude\":80" + count + "}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/helprequests", body)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().HelpRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ServiceAlsoRejectsInvalidCount(int? count)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await Assert.ThrowsAsync<ArgumentException>(() => new HelpRequestService(db).CreateAsync(Guid.NewGuid(), new() { EstimatedPeopleCount = count }));
        Assert.Empty(db.HelpRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(12)]
    public async Task BothReadBoundariesPreserveHistoricalNullOrCount(int? count)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = new HelpRequest { EstimatedPeopleCount = count, VerificationStatus = VerificationStatus.Verified };
        db.HelpRequests.Add(entity); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(count, (await new HelpRequestService(db).GetByIdAsync(entity.Id))!.EstimatedPeopleCount);
        db.ChangeTracker.Clear();
        Assert.Equal(count, (await new HelpRequestReadService(db).GetEligibleAsync(entity.Id))!.EstimatedPeopleCount);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
