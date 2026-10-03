using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Tests;

public sealed class ActionEmailServiceTests : IDisposable
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly RecordingQueue queue = new();
    private readonly RecordingPushNotificationService pushes = new();

    public void Dispose() => db.Dispose();

    private ActionEmailService Service(bool enabled = true) => new(
        db,
        TestDbFactory.Create(),
        queue,
        pushes,
        Options.Create(new EmailOptions { Enabled = enabled }),
        NullLogger<ActionEmailService>.Instance);

    private async Task<User> AddUser(string email, UserRole role, bool notifications = true)
    {
        var user = new User { FullName = $"User {email}", Email = email, PasswordHash = "x", Role = role, EmailNotificationsEnabled = notifications };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task HelpRequestSubmitted_EmailsTheCitizenAndTheHelpRequestManagers()
    {
        var citizen = await AddUser("citizen@example.com", UserRole.Citizen);
        var manager = await AddUser("manager@example.com", UserRole.HelpRequestManager);
        var request = new HelpRequest { CitizenId = citizen.Id, Type = HelpRequestType.Medical, Description = "Needs insulin" };
        db.HelpRequests.Add(request);
        await db.SaveChangesAsync();

        await Service().HelpRequestSubmittedAsync(request.Id);

        Assert.Contains(queue.Sent, m => m.ToAddress == citizen.Email);
        Assert.Contains(queue.Sent, m => m.ToAddress == manager.Email);
        Assert.DoesNotContain(queue.Sent, m => m.ToAddress == "nobody@example.com");
    }

    [Fact]
    public async Task CitizenWhoOptedOutOfEmail_GetsNothingButStaffStillGetTheirCopy()
    {
        var citizen = await AddUser("optedout@example.com", UserRole.Citizen, notifications: false);
        await AddUser("manager@example.com", UserRole.HelpRequestManager);
        var request = new HelpRequest { CitizenId = citizen.Id, Type = HelpRequestType.Rescue, Description = "Trapped" };
        db.HelpRequests.Add(request);
        await db.SaveChangesAsync();

        await Service().HelpRequestSubmittedAsync(request.Id);

        Assert.DoesNotContain(queue.Sent, m => m.ToAddress == citizen.Email);
        Assert.Single(queue.Sent);
    }

    [Fact]
    public async Task IncidentReported_EmailsOnlyTheEmergencyCoordinators()
    {
        var reporter = await AddUser("reporter@example.com", UserRole.Citizen);
        var coordinator = await AddUser("coordinator@example.com", UserRole.EmergencyCoordinator);
        var incident = new Incident { Title = "Flooding", Description = "Water rising", ReportedByUserId = reporter.Id };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        await Service().IncidentReportedAsync(incident.Id);

        var message = Assert.Single(queue.Sent);
        Assert.Equal(coordinator.Email, message.ToAddress);
    }

    [Fact]
    public async Task GlobalSwitchOff_QueuesNothing()
    {
        var citizen = await AddUser("citizen@example.com", UserRole.Citizen);
        var request = new HelpRequest { CitizenId = citizen.Id, Type = HelpRequestType.Food, Description = "Food needed" };
        db.HelpRequests.Add(request);
        await db.SaveChangesAsync();

        await Service(enabled: false).HelpRequestSubmittedAsync(request.Id);

        Assert.Empty(queue.Sent);
    }

    [Fact]
    public async Task CitizenDescriptionIsEscapedInTheHtmlBody()
    {
        var citizen = await AddUser("citizen@example.com", UserRole.Citizen);
        var request = new HelpRequest { CitizenId = citizen.Id, Type = HelpRequestType.Other, Description = "<script>alert(1)</script>" };
        db.HelpRequests.Add(request);
        await db.SaveChangesAsync();

        await Service().HelpRequestSubmittedAsync(request.Id);

        var html = Assert.Single(queue.Sent, m => m.ToAddress == citizen.Email).HtmlBody;
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task HelpRequestStatusChange_IsPushedToTheCitizenEvenWithEmailSwitchedOff()
    {
        var citizen = await AddUser("citizen@example.com", UserRole.Citizen, notifications: false);
        var request = new HelpRequest { CitizenId = citizen.Id, Type = HelpRequestType.Water, Description = "No water" };
        db.HelpRequests.Add(request);
        await db.SaveChangesAsync();

        await Service().HelpRequestStatusChangedAsync(request.Id, "In Progress");

        var (userIds, message) = Assert.Single(pushes.Calls);
        Assert.Equal([citizen.Id], userIds);
        Assert.Equal("Help request update", message.Title);
        Assert.Empty(queue.Sent);
    }

    private sealed class RecordingQueue : IEmailQueue
    {
        public List<EmailMessage> Sent { get; } = [];

        public bool TryQueue(EmailMessage message)
        {
            Sent.Add(message);
            return true;
        }
    }
}
