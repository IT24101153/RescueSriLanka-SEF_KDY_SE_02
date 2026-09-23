using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// District matching decides whether a warning reaches anyone at all: a citizen
/// picks from a list, a reporter types free text, and the two have to meet.
/// </summary>
public class SriLankaDistrictsTests
{
    [Fact]
    public void AllTwentyFiveDistrictsArePresentAndUnique()
    {
        Assert.Equal(25, SriLankaDistricts.All.Count);
        Assert.Equal(25, SriLankaDistricts.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("Colombo", "Colombo")]
    [InlineData("colombo", "Colombo")]
    [InlineData("  KANDY  ", "Kandy")]
    [InlineData("Nuwara-Eliya", "Nuwara Eliya")]
    [InlineData("nuwara   eliya", "Nuwara Eliya")]
    [InlineData("Galle District", "Galle")]
    [InlineData("matara district", "Matara")]
    public void NormaliseAcceptsTheSpellingsPeopleActuallyType(string input, string expected)
    {
        Assert.Equal(expected, SriLankaDistricts.Normalise(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Chennai")]
    [InlineData("Western Province")]
    public void NormaliseRejectsAnythingThatIsNotADistrict(string? input)
    {
        Assert.Null(SriLankaDistricts.Normalise(input));
    }

    [Fact]
    public void EveryDistrictSurvivesARoundTrip()
    {
        foreach (var district in SriLankaDistricts.All)
        {
            Assert.Equal(district, SriLankaDistricts.Normalise(district.ToLowerInvariant()));
        }
    }

    // ------------------------------------------------------------- testmail

    [Theory]
    [InlineData("priya@gmail.com", "priyagmailcom")]
    [InlineData("Coordinator@Rescue.LK", "coordinatorrescuelk")]
    [InlineData("a.b+tag@example.com", "abtagexamplecom")]
    public void TestmailTagsAreAlphanumericAndStable(string email, string expected)
    {
        Assert.Equal(expected, TestmailOptions.TagFor(email));
    }

    [Fact]
    public void TestmailAddressUsesTheNamespace()
    {
        var options = new TestmailOptions { Namespace = "abc12" };

        Assert.Equal(
            "abc12.priyagmailcom@inbox.testmail.app",
            options.AddressFor(TestmailOptions.TagFor("priya@gmail.com")));
    }

    [Fact]
    public async Task RedirectRewritesTheRecipientAndNothingElse()
    {
        var inner = new CapturingSender();
        var redirecting = new TestmailRedirectingEmailSender(
            inner,
            Options.Create(new EmailOptions
            {
                Testmail = new TestmailOptions { Namespace = "zw3gb" }
            }),
            NullLogger<TestmailRedirectingEmailSender>.Instance);

        await redirecting.SendAsync(new EmailMessage
        {
            ToAddress = "priya@gmail.com",
            ToName = "Priya",
            Subject = "CRITICAL warning — Flood in Colombo",
            HtmlBody = "<p>Move to higher ground.</p>",
            TextBody = "Move to higher ground."
        });

        var delivered = Assert.Single(inner.Sent);

        Assert.Equal("zw3gb.priyagmailcom@inbox.testmail.app", delivered.ToAddress);
        // The person is still addressed by name, and the warning is untouched —
        // only the envelope changes.
        Assert.Equal("Priya", delivered.ToName);
        Assert.Equal("CRITICAL warning — Flood in Colombo", delivered.Subject);
        Assert.Equal("Move to higher ground.", delivered.TextBody);
    }

    private sealed class CapturingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public string Name => "capturing (test)";

        public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(true);
        }
    }
}
