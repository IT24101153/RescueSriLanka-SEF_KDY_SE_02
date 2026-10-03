using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;

namespace RescueSriLanka.Api.Tests;

/// <summary>What goes to Gemini, and how its answer comes back. No network: a stub handler answers.</summary>
public class GeminiPlanningModelTests
{
    [Fact]
    public async Task TheKeyTravelsInAHeaderAndTheConfiguredSettingsAreSent()
    {
        var handler = new StubHandler(_ => Json(TextTurn("{}")));
        var model = Model(handler, new Dictionary<string, string?>
        {
            ["GoogleAi:ApiKey"] = "test-key",
            ["GoogleAi:Model"] = "gemini-3-flash-preview",
            ["GoogleAi:MaxOutputTokens"] = "2048",
            ["GoogleAi:ThinkingBudget"] = "0"   // 0 is not allowed on this model, so it is floored at 128.
        });

        await model.NextTurnAsync([], "instruction");

        var sent = handler.Requests.Single();
        Assert.Equal("test-key", sent.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("key=", sent.RequestUri!.Query);
        Assert.Contains("models/gemini-3-flash-preview:generateContent", sent.RequestUri.AbsolutePath);

        var body = JsonDocument.Parse(handler.Bodies.Single()).RootElement;
        var generation = body.GetProperty("generationConfig");
        Assert.Equal(2048, generation.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(128, generation.GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
        var declared = body.GetProperty("tools")[0].GetProperty("functionDeclarations").EnumerateArray()
            .Select(declaration => declaration.GetProperty("name").GetString()!)
            .ToList();
        Assert.Equal(AssessmentTools.AllowList.ToList(), declared);
    }

    [Fact]
    public async Task AFunctionCallAndTextAreReadFromTheAnswer()
    {
        var handler = new StubHandler(_ => Json(CallTurn("count_nearby_active_incidents", new { radius_km = 3 })));
        var model = Model(handler, Settings());

        var turn = await model.NextTurnAsync([], "instruction");

        var call = Assert.Single(turn.Calls);
        Assert.Equal("count_nearby_active_incidents", call.Name);
        Assert.Equal(3, call.Arguments.GetProperty("radius_km").GetInt32());
        Assert.Null(turn.Text);
    }

    [Fact]
    public async Task ATransientErrorIsRetriedAndThenAnswered()
    {
        var replies = new Queue<HttpResponseMessage>([
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            Json(TextTurn("{\"priority\":\"High\"}"))
        ]);
        var handler = new StubHandler(_ => replies.Dequeue());
        var model = Model(handler, Settings());

        var turn = await model.NextTurnAsync([], "instruction");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("priority", turn.Text);
    }

    [Fact]
    public async Task AClientErrorIsNotRetried()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var model = Model(handler, Settings());

        await Assert.ThrowsAsync<HttpRequestException>(() => model.NextTurnAsync([], "instruction"));
        Assert.Single(handler.Requests);
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["GoogleAi:ApiKey"] = "test-key",
        ["GoogleAi:Model"] = "gemini-3-flash-preview",
        ["GoogleAi:MaxAttempts"] = "3"
    };

    private static GeminiPlanningModel Model(StubHandler handler, Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new GeminiPlanningModel(new HttpClient(handler), config);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string TextTurn(string text) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { role = "model", parts = new[] { new { text } } } } }
    });

    private static string CallTurn(string name, object args) => JsonSerializer.Serialize(new
    {
        candidates = new[]
        {
            new { content = new { role = "model", parts = new[] { new { functionCall = new { name, args } } } } }
        }
    });

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            return respond(request);
        }
    }
}
