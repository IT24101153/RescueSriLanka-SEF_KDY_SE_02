using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// Who gets warned, and who does not.
///
/// This is the part of the email feature worth testing: the templates are text
/// and SMTP belongs to MailKit, but "which citizens does a Critical flood in
/// Kandy reach?" is our rule, and getting it wrong means either warning nobody
/// or emailing the wrong district.
/// </summary>
public class NotificationServiceTests
{
    /// <summary>Records what it was asked to send instead of sending it.</summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public string Name => "recording (test)";

        public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(true);
        }
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"notify-{Guid.NewGuid()}")
            .Options);

    private static (NotificationService Service, RecordingEmailSender Sender) NewService(
        AppDbContext db, IncidentSeverity minimum = IncidentSeverity.High)
    {
        var sender = new RecordingEmailSender();
        var options = Options.Create(new EmailOptions { MinimumWarningSeverity = minimum });

        return (
            new NotificationService(db, sender, options, NullLogger<NotificationService>.Instance),
            sender);
    }

    private static User NewUser(
        string email,
        string? district = "Colombo",
        bool notifications = true,
        bool isActive = true) => new()
        {
            FullName = "Test Citizen",
            Email = email,
            PasswordHash = "-",
            District = district,
            EmailNotificationsEnabled = notifications,
            IsActive = isActive
        };

    private static Incident NewIncident(
        IncidentSeverity severity = IncidentSeverity.Critical,
        string? district = "Colombo",
        bool isActive = true) => new()
        {
            Title = "Flash flooding on Galle Road",
            Description = "Water over the carriageway.",
            Type = IncidentType.Flood,
            Severity = severity,
            District = district,
            IsActive = isActive
        };

    // ------------------------------------------------------------ warnings

    [Fact]
    public async Task Warning_ReachesEverySubscriberInTheDistrict()
    {
        using var db = NewDb();
        var incident = NewIncident();
        db.Incidents.Add(incident);
        db.Users.AddRange(
            NewUser("a@example.com"),
            NewUser("b@example.com"),
            NewUser("elsewhere@example.com", district: "Kandy"));
        await db.SaveChangesAsync();

        var (service, sender) = NewService(db);
        var sent = await service.SendDistrictWarningAsync(incident.Id);

        Assert.Equal(2, sent);
        Assert.Equal(
            ["a@example.com", "b@example.com"],
            sender.Sent.Select(message => message.ToAddress).Order());
    }

    [Theory]
    [InlineData(IncidentSeverity.Low, 0)]
    [InlineData(IncidentSeverity.Moderate, 0)]
    [InlineData(IncidentSeverity.High, 1)]
    [InlineData(IncidentSeverity.Critical, 1)]
    public async Task Warning_ObeysTheSeverityThreshold(IncidentSeverity severity, int expected)
    {
        using var db = NewDb();
        var incident = NewIncident(severity);
        db.Incidents.Add(incident);
        db.Users.Add(NewUser("citizen@example.com"));
        await db.SaveChangesAsync();

        var (service, _) = NewService(db);

        Assert.Equal(expected, await service.SendDistrictWarningAsync(incident.Id));
    }

    [Fact]
    public async Task Warning_IsSentOnlyOncePerIncident()
    {
        using var db = NewDb();
        var incident = NewIncident();
        db.Incidents.Add(incident);
        db.Users.Add(NewUser("citizen@example.com"));
        await db.SaveChangesAsync();

        var (service, sender) = NewService(db);

        // Verification and AI approval both fire this; the district hears once.
        await service.SendDistrictWarningAsync(incident.Id);
        var second = await service.SendDistrictWarningAsync(incident.Id);

        Assert.Equal(0, second);
        Assert.Single(sender.Sent);
        Assert.NotNull(db.Incidents.Single().DistrictWarningSentAt);
    }

    [Fact]
    public async Task Warning_MatchesDistrictsWrittenInAnyCase()
    {
        using var db = NewDb();
        // What a reporter typed, against what the citizen picked from the list.
        var incident = NewIncident(district: "nuwara-eliya");
        db.Incidents.Add(incident);
        db.Users.Add(NewUser("citizen@example.com", district: "Nuwara Eliya"));
        await db.SaveChangesAsync();

        var (service, _) = NewService(db);

        Assert.Equal(1, await service.SendDistrictWarningAsync(incident.Id));
    }

    [Fact]
    public async Task Warning_SkipsOptedOutDisabledAndDistrictlessAccounts()
    {
        using var db = NewDb();
        var incident = NewIncident();
        db.Incidents.Add(incident);
        db.Users.AddRange(
            NewUser("optedout@example.com", notifications: false),
            NewUser("disabled@example.com", isActive: false),
            NewUser("nodistrict@example.com", district: null),
            NewUser("wants-it@example.com"));
        await db.SaveChangesAsync();

        var (service, sender) = NewService(db);
        await service.SendDistrictWarningAsync(incident.Id);

        Assert.Equal("wants-it@example.com", Assert.Single(sender.Sent).ToAddress);
    }

    [Fact]
    public async Task Warning_IsNotStampedWhenNobodyIsSubscribed()
    {
        using var db = NewDb();
        var incident = NewIncident();
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var (service, _) = NewService(db);
        await service.SendDistrictWarningAsync(incident.Id);

        // Leaving it unstamped means someone who sets their district a minute
        // from now is still warned by the next trigger.
        Assert.Null(db.Incidents.Single().DistrictWarningSentAt);
    }

    [Fact]
    public async Task Warning_IsSkippedForAnUnrecognisedDistrict()
    {
        using var db = NewDb();
        var incident = NewIncident(district: "Somewhere Else");
        db.Incidents.Add(incident);
        db.Users.Add(NewUser("citizen@example.com"));
        await db.SaveChangesAsync();

        var (service, _) = NewService(db);

        Assert.Equal(0, await service.SendDistrictWarningAsync(incident.Id));
    }

    [Fact]
    public async Task Warning_IsSkippedForAClosedIncident()
    {
        using var db = NewDb();
        var incident = NewIncident(isActive: false);
        db.Incidents.Add(incident);
        db.Users.Add(NewUser("citizen@example.com"));
        await db.SaveChangesAsync();

        var (service, _) = NewService(db);

        Assert.Equal(0, await service.SendDistrictWarningAsync(incident.Id));
    }

    [Fact]
    public async Task Warning_NamesTheRiskLevelInTheSubject()
    {
        using var db = NewDb();
        var incident = NewIncident();
        db.Incidents.Add(incident);
        db.Users.Add(NewUser("citizen@example.com"));
        await db.SaveChangesAsync();

        var (service, sender) = NewService(db);
        await service.SendDistrictWarningAsync(incident.Id);

        var message = Assert.Single(sender.Sent);
        Assert.Contains("CRITICAL", message.Subject);
        Assert.Contains("Colombo", message.Subject);
        // Colour is never the only carrier of severity — the word is in the body.
        Assert.Contains("Critical", message.TextBody);
    }

    // ------------------------------------------------------------- receipts

    [Fact]
    public async Task Receipt_GoesToTheReporter()
    {
        using var db = NewDb();
        var reporter = NewUser("reporter@example.com");
        db.Users.Add(reporter);

        var incident = NewIncident();
        incident.ReportedByUserId = reporter.Id;
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var (service, sender) = NewService(db);
        await service.SendReportReceivedAsync(incident.Id);

        var message = Assert.Single(sender.Sent);
        Assert.Equal("reporter@example.com", message.ToAddress);
        Assert.Contains("Report received", message.Subject);
    }

    [Fact]
    public async Task Receipt_IsSkippedForAnAnonymousReport()
    {
        using var db = NewDb();
        var incident = NewIncident();
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var (service, _) = NewService(db);

        Assert.Equal(0, await service.SendReportReceivedAsync(incident.Id));
    }

    [Fact]
    public async Task NothingIsSentWhenEmailIsTurnedOffEntirely()
    {
        using var db = NewDb();
        var incident = NewIncident();
        db.Incidents.Add(incident);
        db.Users.Add(NewUser("citizen@example.com"));
        await db.SaveChangesAsync();

        var sender = new RecordingEmailSender();
        var service = new NotificationService(
            db,
            sender,
            Options.Create(new EmailOptions { Enabled = false }),
            NullLogger<NotificationService>.Instance);

        await service.SendDistrictWarningAsync(incident.Id);

        Assert.Empty(sender.Sent);
    }
}
