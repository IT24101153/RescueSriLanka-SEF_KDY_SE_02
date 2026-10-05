using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Tests;

public sealed class NoOpActionEmailService : IActionEmailService
{
    public Task IncidentReportedAsync(Guid incidentId, CancellationToken ct = default) => Task.CompletedTask;
    public Task IncidentStatusChangedAsync(Guid incidentId, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task IncidentSeverityOverriddenAsync(Guid incidentId, string severity, CancellationToken ct = default) => Task.CompletedTask;
    public Task HelpRequestSubmittedAsync(Guid helpRequestId, CancellationToken ct = default) => Task.CompletedTask;
    public Task HelpRequestVerifiedAsync(Guid helpRequestId, bool isReal, CancellationToken ct = default) => Task.CompletedTask;
    public Task HelpRequestStatusChangedAsync(Guid helpRequestId, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task HelpRequestGuidanceAsync(Guid helpRequestId, bool critical, CancellationToken ct = default) => Task.CompletedTask;
    public Task HelpRequestCancelledAsync(Guid helpRequestId, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResourceRequestSubmittedAsync(Guid resourceRequestId, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResourceRequestStatusChangedAsync(Guid resourceRequestId, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task DonationSubmittedAsync(Guid donationId, CancellationToken ct = default) => Task.CompletedTask;
    public Task DonationStatusChangedAsync(Guid donationId, string status, CancellationToken ct = default) => Task.CompletedTask;
    public Task AssignmentCreatedAsync(Guid assignmentId, CancellationToken ct = default) => Task.CompletedTask;
    public Task AssignmentDecidedAsync(Guid assignmentId, string decision, CancellationToken ct = default) => Task.CompletedTask;
    public Task DispatchStatusChangedAsync(Guid dispatchId, string status, CancellationToken ct = default) => Task.CompletedTask;
}
