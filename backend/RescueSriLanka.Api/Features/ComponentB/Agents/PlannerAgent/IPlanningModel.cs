using System.Text.Json;

namespace RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;

/// <summary>One model turn: either tool calls it wants answered, or its final text.</summary>
/// <param name="ModelContent">The model's own turn, echoed back so it can follow its calls.</param>
public sealed record PlanningTurn(IReadOnlyList<PlannedToolCall> Calls, string? Text, JsonElement ModelContent);

public sealed record PlannedToolCall(string Name, JsonElement Arguments);

/// <summary>
/// A function-calling model. It sits behind this interface so tests can script a
/// conversation, and so the agent never depends on one vendor's wire format.
/// </summary>
public interface IPlanningModel
{
    bool IsConfigured { get; }

    Task<PlanningTurn> NextTurnAsync(
        IReadOnlyList<object> contents, string instruction, CancellationToken ct = default);
}
