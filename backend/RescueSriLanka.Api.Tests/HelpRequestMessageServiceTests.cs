using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Tests;

public class HelpRequestMessageServiceTests
{
    [Fact]
    public async Task AddAsync_StoresTheGuidanceTrimmedAndNotifiesTheCitizen()
    {
        await using var db = CreateContext();
        var request = await AddRequestAsync(db);
        var emails = new RecordingActionEmailService();
        var service = new HelpRequestMessageService(db, emails);

        var saved = await service.AddAsync(request.Id, Guid.NewGuid(), new CreateHelpRequestMessageDto
        {
            Message = "  Help is being arranged.  ",
            DoItems = ["Move to the upper floor", "  ", "Keep your phone charged"],
            DontItems = ["Do not go outside"],
            IsCritical = true
        });

        Assert.NotNull(saved);
        Assert.Equal("Help is being arranged.", saved.Message);
        Assert.Equal(["Move to the upper floor", "Keep your phone charged"], saved.DoItems);
        Assert.Equal(["Do not go outside"], saved.DontItems);
        Assert.Equal([(request.Id, true)], emails.HelpRequestGuidance);
        Assert.Equal(1, await db.HelpRequestMessages.CountAsync());
    }

    [Fact]
    public async Task AddAsync_RejectsAnEmptyMessage()
    {
        await using var db = CreateContext();
        var request = await AddRequestAsync(db);
        var service = new HelpRequestMessageService(db, new RecordingActionEmailService());

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            request.Id, Guid.NewGuid(),
            new CreateHelpRequestMessageDto { Message = "   ", DoItems = [" "], DontItems = [] }));
    }

    [Fact]
    public async Task AddAsync_RejectsAnOversizedItem()
    {
        await using var db = CreateContext();
        var request = await AddRequestAsync(db);
        var service = new HelpRequestMessageService(db, new RecordingActionEmailService());

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            request.Id, Guid.NewGuid(),
            new CreateHelpRequestMessageDto { DoItems = [new string('x', HelpRequestMessageService.MaxItemLength + 1)] }));
    }

    [Fact]
    public async Task AddAsync_ReturnsNullForAMissingRequestAndSendsNothing()
    {
        await using var db = CreateContext();
        var emails = new RecordingActionEmailService();
        var service = new HelpRequestMessageService(db, emails);

        var saved = await service.AddAsync(Guid.NewGuid(), Guid.NewGuid(),
            new CreateHelpRequestMessageDto { Message = "Hello" });

        Assert.Null(saved);
        Assert.Empty(emails.HelpRequestGuidance);
    }

    [Fact]
    public async Task GetAsync_PutsUrgentGuidanceFirstThenNewestFirst()
    {
        await using var db = CreateContext();
        var request = await AddRequestAsync(db);
        var otherRequest = await AddRequestAsync(db);
        var now = DateTime.UtcNow;
        db.HelpRequestMessages.AddRange(
            Message(request.Id, "old note", false, now.AddMinutes(-30)),
            Message(request.Id, "new note", false, now),
            Message(request.Id, "urgent, but older", true, now.AddMinutes(-10)),
            Message(otherRequest.Id, "someone else's", false, now));
        await db.SaveChangesAsync();

        var messages = await new HelpRequestMessageService(db, new RecordingActionEmailService())
            .GetAsync(request.Id);

        Assert.Equal(["urgent, but older", "new note", "old note"], messages.Select(m => m.Message));
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyThatRequestsMessage()
    {
        await using var db = CreateContext();
        var request = await AddRequestAsync(db);
        var other = await AddRequestAsync(db);
        var mine = Message(request.Id, "mine", false, DateTime.UtcNow);
        db.HelpRequestMessages.Add(mine);
        await db.SaveChangesAsync();
        var service = new HelpRequestMessageService(db, new RecordingActionEmailService());

        Assert.False(await service.DeleteAsync(other.Id, mine.Id));
        Assert.True(await service.DeleteAsync(request.Id, mine.Id));
        Assert.Empty(db.HelpRequestMessages);
    }

    [Fact]
    public async Task EditingARequestQueuesItForTriageAgain()
    {
        await using var db = CreateContext();
        var citizenId = Guid.NewGuid();
        var queue = new RecordingQueue();
        var service = new HelpRequestService(db, queue);
        var created = await service.CreateAsync(citizenId, new CreateHelpRequestDto
        {
            Type = HelpRequestType.Water, Description = "Need water", Latitude = 7, Longitude = 80, EstimatedPeopleCount = 2
        });
        queue.Enqueued.Clear();

        await service.UpdateAsync(created.Id, citizenId, new UpdateHelpRequestDto
        {
            Type = HelpRequestType.Medical, Description = "Someone is injured", Latitude = 7, Longitude = 80, EstimatedPeopleCount = 2
        });

        Assert.Equal([created.Id], queue.Enqueued);
    }

    private sealed class RecordingQueue : IHelpRequestAnalysisQueue
    {
        public List<Guid> Enqueued { get; } = [];
        public void Enqueue(Guid helpRequestId) => Enqueued.Add(helpRequestId);
    }

    private static HelpRequestMessage Message(Guid requestId, string text, bool critical, DateTime at) => new()
    {
        HelpRequestId = requestId, AuthorUserId = Guid.NewGuid(), Message = text, IsCritical = critical, CreatedAt = at
    };

    private static async Task<HelpRequest> AddRequestAsync(AppDbContext db)
    {
        var request = new HelpRequest { CitizenId = Guid.NewGuid(), Type = HelpRequestType.Rescue, Description = "Trapped", EstimatedPeopleCount = 2 };
        db.HelpRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
