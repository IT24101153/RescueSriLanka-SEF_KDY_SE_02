using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Tests;

/// <summary>Records the help-request status emails the planner asks for. Everything else is a no-op.</summary>
public sealed class RecordingActionEmailService : IActionEmailService
{
    public List<(Guid HelpRequestId, string Status)> HelpRequestStatusChanges { get; } = [];

    public Task HelpRequestStatusChangedAsync(Guid helpRequestId, string status, CancellationToken ct = default)
    {
        HelpRequestStatusChanges.Add((helpRequestId, status));
        return Task.CompletedTask;
    }

    public Task IncidentReportedAsync(Guid incidentId, CancellationToken ct = default) => Task.CompletedTask;
    public Task IncidentStatusChangedAsync(Guid incidentId, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task IncidentSeverityOverriddenAsync(Guid incidentId, string severity, CancellationToken ct = default) => Task.CompletedTask;
    public Task HelpRequestSubmittedAsync(Guid helpRequestId, CancellationToken ct = default) => Task.CompletedTask;
    public Task HelpRequestVerifiedAsync(Guid helpRequestId, bool isReal, CancellationToken ct = default) => Task.CompletedTask;
    public Task HelpRequestCancelledAsync(Guid helpRequestId, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResourceRequestSubmittedAsync(Guid resourceRequestId, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResourceRequestStatusChangedAsync(Guid resourceRequestId, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task DonationSubmittedAsync(Guid donationId, CancellationToken ct = default) => Task.CompletedTask;
    public Task DonationStatusChangedAsync(Guid donationId, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task AssignmentCreatedAsync(Guid assignmentId, CancellationToken ct = default) => Task.CompletedTask;
    public Task AssignmentDecidedAsync(Guid assignmentId, string decision, CancellationToken ct = default) => Task.CompletedTask;
    public Task DispatchStatusChangedAsync(Guid dispatchId, string status, CancellationToken ct = default) => Task.CompletedTask;
}
