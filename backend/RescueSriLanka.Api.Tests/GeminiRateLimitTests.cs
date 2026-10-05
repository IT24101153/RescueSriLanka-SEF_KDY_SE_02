using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Services.Llm;

namespace RescueSriLanka.Api.Tests;

// Gemini answers 429 when the key's quota runs out. Every AI feature shares one key, so a
// 429 has to be told apart: wait a short time, report a long wait, or stop on a daily quota.
public class GeminiRateLimitTests
{
    private const string Success = """{"candidates":[{"finishReason":"STOP","content":{"role":"model","parts":[{"text":"ok"}]}}]}""";

    private static string RateLimited(string retryDelay) => $$$"""
        {"error":{"code":429,"status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"{{{retryDelay}}}"}]}}
        """;

    private const string DailyQuota = """
        {"error":{"code":429,"status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier"}]}]}}
        """;

    [Fact]
    public async Task GoogleAiClient_WaitsForTheTimeGoogleAsksForThenSucceeds()
    {
        var handler = new SequenceHandler((HttpStatusCode.TooManyRequests, RateLimited("0.01s")), (HttpStatusCode.OK, Success));
        var client = NewClient(handler);

        var text = await client.GenerateAsync("system", "prompt");

        Assert.Equal("ok", text);
        Assert.Equal(2, client.LastAttempts);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GoogleAiClient_StopsAtOnceWhenTheDailyQuotaIsUsedUp()
    {
        var handler = new SequenceHandler((HttpStatusCode.TooManyRequests, DailyQuota));
        var client = NewClient(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.GenerateAsync("system", "prompt"));

        Assert.Contains("daily quota", error.Message);
        Assert.DoesNotContain("HTTP 429", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GoogleAiClient_ReportsALongWaitInsteadOfHoldingTheUserUp()
    {
        var handler = new SequenceHandler((HttpStatusCode.TooManyRequests, RateLimited("60s")));
        var client = NewClient(handler);

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.GenerateAsync("system", "prompt"));

        Assert.Contains("rate limiting", error.Message);
        Assert.Contains("60 seconds", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GoogleAiClient_GivesAFriendlyMessageWhenEveryAttemptIsRateLimited()
    {
        var handler = new SequenceHandler((HttpStatusCode.TooManyRequests, RateLimited("0.01s")));
        var client = NewClient(handler, ("GoogleAi:MaxAttempts", "2"));

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => client.GenerateAsync("system", "prompt"));

        Assert.Contains("rate limiting", error.Message);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GoogleAiClient_RetriesAServerErrorButNotARejectedRequest()
    {
        var recovers = new SequenceHandler((HttpStatusCode.ServiceUnavailable, "{}"), (HttpStatusCode.OK, Success));
        Assert.Equal("ok", await NewClient(recovers).GenerateAsync("system", "prompt"));

        var rejected = new SequenceHandler((HttpStatusCode.BadRequest, "{}"));
        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => NewClient(rejected).GenerateAsync("system", "prompt"));
        Assert.Contains("HTTP 400", error.Message);
        Assert.Equal(1, rejected.Calls);
    }

    [Fact]
    public async Task PlanningModel_TellsThePersonWhenTheDailyQuotaIsUsedUp()
    {
        var handler = new SequenceHandler((HttpStatusCode.TooManyRequests, DailyQuota));
        var model = new GeminiPlanningModel(new HttpClient(handler), Config());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => model.NextTurnAsync([], "instruction"));

        Assert.Contains("daily quota", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task PlanningModel_WaitsOutAShortRateLimit()
    {
        var handler = new SequenceHandler((HttpStatusCode.TooManyRequests, RateLimited("0.01s")), (HttpStatusCode.OK, Success));
        var model = new GeminiPlanningModel(new HttpClient(handler), Config());

        var turn = await model.NextTurnAsync([], "instruction");

        Assert.Equal("ok", turn.Text);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("37s", 37)]
    [InlineData("0.5s", 0.5)]
    public void ParseRetryDelay_ReadsGoogleSuggestion(string value, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), GeminiThrottle.ParseRetryDelay(RateLimited(value)));

    [Fact]
    public void ParseRetryDelay_AndIsDailyQuota_IgnoreABodyThatIsNotJson()
    {
        Assert.Null(GeminiThrottle.ParseRetryDelay("<html>busy</html>"));
        Assert.False(GeminiThrottle.IsDailyQuota("<html>busy</html>"));
    }

    private static IConfiguration Config(params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?> { ["GoogleAi:ApiKey"] = "test-key", ["GoogleAi:MaxAttempts"] = "3" };
        foreach (var (key, value) in extra) settings[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static GoogleAiClient NewClient(SequenceHandler handler, params (string Key, string Value)[] extra) =>
        new(new HttpClient(handler), Config(extra), NullLogger<GoogleAiClient>.Instance);

    // Answers each call from the list in turn; the last answer repeats.
    private sealed class SequenceHandler(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = answers[Math.Min(Calls, answers.Length - 1)];
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
