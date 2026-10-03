using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentA.DTOs;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Features.ComponentD.Data;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Tests;

/// <summary>
/// Runs only when TEST_POSTGRES points at a PostgreSQL server (any database on
/// it; the tests create and drop their own). Everything else in the suite uses
/// the in-memory provider, which has no real migrations, no transactions and no
/// row-level concurrency — exactly what these tests exist to prove.
///
///   TEST_POSTGRES="Host=localhost;Username=me;Database=postgres" dotnet test --filter PostgresIntegration
///
/// Never point this at a real environment: each test creates and drops a database.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(PostgresDatabase.AdminConnectionString))
        {
            Skip = $"Set {PostgresDatabase.EnvironmentVariable} to a PostgreSQL connection string to run.";
        }
    }
}

/// <summary>A throwaway database, dropped when the test is done.</summary>
public sealed class PostgresDatabase : IAsyncDisposable
{
    public const string EnvironmentVariable = "TEST_POSTGRES";

    public static string? AdminConnectionString => Environment.GetEnvironmentVariable(EnvironmentVariable);

    public string ConnectionString { get; }
    private readonly string name = $"rsl_test_{Guid.NewGuid():N}";

    private PostgresDatabase()
    {
        ConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = name }.ConnectionString;
    }

    public static async Task<PostgresDatabase> CreateAsync()
    {
        var database = new PostgresDatabase();
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database.name}\"", connection);
        await command.ExecuteNonQueryAsync();
        return database;
    }

    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public ComponentDDbContext NewComponentDContext() =>
        new(new DbContextOptionsBuilder<ComponentDDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Applies the schema exactly as the API does on start-up.</summary>
    public async Task MigrateAsync()
    {
        await using var app = NewContext();
        await app.Database.MigrateAsync();
        await using var componentD = NewComponentDContext();
        await componentD.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

public class PostgresIntegrationTests
{
    /// <summary>The last AppDbContext migration before AddAgentRunPlanAndDecision.</summary>
    private const string MigrationBeforeDecision = "20260928101228_RemoveComponentCShelters";

    private static readonly Guid Coordinator = Guid.NewGuid();

    private sealed class RecordingQueue : INotificationQueue
    {
        public ConcurrentBag<NotificationJob> Jobs { get; } = [];
        public void Enqueue(NotificationJob job) => Jobs.Add(job);
    }

    private sealed class FailingZones : ISafetyZoneService
    {
        public Task<IReadOnlyList<SafetyZoneDto>> GetActiveAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ZoneCheckResultDto> CheckPointAsync(double latitude, double longitude, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<int> RecomputeAsync(CancellationToken ct = default) =>
            throw new IOException("zone store down");
    }

    private static AgentRunService NewService(AppDbContext db, INotificationQueue queue, ISafetyZoneService? zones = null) =>
        new(db, zones ?? new SafetyZoneService(db, NullLogger<SafetyZoneService>.Instance), queue,
            NullLogger<AgentRunService>.Instance);

    /// <summary>An analysed incident and its undecided run.</summary>
    private static async Task<(Guid IncidentId, Guid RunId)> SeedProposalAsync(PostgresDatabase database)
    {
        await using var db = database.NewContext();
        var incident = new Incident
        {
            Title = "Flood in Kaduwela",
            Description = "Water rising.",
            Type = IncidentType.Flood,
            Severity = IncidentSeverity.Moderate,
            AiSeverity = IncidentSeverity.High,
            Status = IncidentStatus.Verified,
            Latitude = 6.9271,
            Longitude = 79.8612,
            AffectedRadiusMeters = 1000,
            District = "Colombo"
        };
        var run = new AgentRun
        {
            AgentName = "IncidentAnalysisAgent",
            Objective = "Classify severity",
            IncidentId = incident.Id,
            Status = AgentRunStatus.Succeeded,
            OutputJson = """{ "severity": "High", "recommendedRadiusMeters": 3000 }"""
        };
        db.Incidents.Add(incident);
        db.AgentRuns.Add(run);
        await db.SaveChangesAsync();
        return (incident.Id, run.Id);
    }

    private static async Task<List<(string Name, string Type, string? Default, bool Nullable)>> ColumnsAsync(
        PostgresDatabase database, string table)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_name, data_type, column_default, is_nullable FROM information_schema.columns " +
            "WHERE table_name = @table", connection);
        command.Parameters.AddWithValue("table", table);

        var columns = new List<(string, string, string?, bool)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add((reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3) == "YES"));
        }

        return columns;
    }

    // ---------- migrations ----------

    [PostgresFact]
    public async Task EveryMigration_AppliesToAnEmptyDatabase_AndTheModelHasNoPendingChanges()
    {
        await using var database = await PostgresDatabase.CreateAsync();

        await database.MigrateAsync();

        await using var db = database.NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(),
            "The EF model differs from the migrations — a migration is missing.");

        var columns = await ColumnsAsync(database, "agent_runs");
        var decision = Assert.Single(columns, c => c.Name == "Decision");
        Assert.False(decision.Nullable);
        Assert.Contains("Pending", decision.Default);
        Assert.Contains(columns, c => c.Name == "PlanJson" && c.Nullable);
        Assert.Contains(columns, c => c.Name == "DecisionNote" && c.Nullable);
        Assert.Contains(columns, c => c.Name == "ModelAttempts" && !c.Nullable);
    }

    [PostgresFact]
    public async Task TheDecisionMigration_BackfillsRunsDecidedBeforeItExisted()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        await using (var before = database.NewContext())
        {
            await before.GetService<IMigrator>().MigrateAsync(MigrationBeforeDecision);

            // Rows as the previous schema stored them: "Approved" was the only record,
            // and a rejection was Approved = false with an ApprovedAt.
            foreach (var (approved, decided) in new[] { (true, true), (false, true), (false, false) })
            {
                await before.Database.ExecuteSqlRawAsync(
                    $"""
                    INSERT INTO agent_runs ("Id","AgentName","Objective","Status","UsedFallback","Approved",
                                            "ApprovedAt","DurationMs","StartedAt")
                    VALUES ('{Guid.NewGuid()}', 'IncidentAnalysisAgent', 'old run', 'Succeeded', FALSE,
                            {(approved ? "TRUE" : "FALSE")}, {(decided ? "NOW()" : "NULL")}, 0, NOW())
                    """);
            }
        }

        await database.MigrateAsync();

        await using var db = database.NewContext();
        var decisions = await db.AgentRuns.AsNoTracking()
            .Select(run => new { run.Approved, run.ApprovedAt, run.Decision })
            .ToListAsync();

        Assert.Equal(3, decisions.Count);
        Assert.Single(decisions, r => r.Approved && r.Decision == AgentRunDecision.Approved);
        Assert.Single(decisions, r => !r.Approved && r.ApprovedAt != null && r.Decision == AgentRunDecision.Rejected);
        Assert.Single(decisions, r => !r.Approved && r.ApprovedAt == null && r.Decision == AgentRunDecision.Pending);
    }

    // ---------- the approval gate on a real database ----------

    [PostgresFact]
    public async Task TwoCoordinatorsApprovingAtOnce_ExactlyOneWins_AndOnlyOneWarningIsQueued()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        await database.MigrateAsync();

        // The race is timing dependent, so repeat it: whichever way each round
        // interleaves, the outcome must be the same.
        for (var round = 0; round < 10; round++)
        {
            var (_, runId) = await SeedProposalAsync(database);
            var queue = new RecordingQueue();

            async Task<Exception?> TryApprove()
            {
                await using var db = database.NewContext();
                try
                {
                    await NewService(db, queue).ApproveAsync(runId, null, Coordinator);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            var outcomes = await Task.WhenAll(Task.Run(TryApprove), Task.Run(TryApprove));

            Assert.Single(outcomes, outcome => outcome is null);
            Assert.Single(outcomes, outcome => outcome is InvalidOperationException);
            Assert.Single(queue.Jobs);

            await using var check = database.NewContext();
            Assert.Equal(AgentRunDecision.Approved,
                (await check.AgentRuns.AsNoTracking().SingleAsync(run => run.Id == runId)).Decision);
        }
    }

    [PostgresFact]
    public async Task ADecisionBasedOnAStaleRead_IsRejectedByTheDatabase_ThroughTheConcurrencyToken()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        await database.MigrateAsync();
        var (_, runId) = await SeedProposalAsync(database);

        // Coordinator B reads the undecided run...
        await using var staleReader = database.NewContext();
        var stale = await staleReader.AgentRuns.SingleAsync(run => run.Id == runId);

        // ...coordinator A decides it first...
        await using (var winner = database.NewContext())
        {
            await NewService(winner, new RecordingQueue()).ApproveAsync(runId, null, Coordinator);
        }

        // ...so B's write, built on the stale read, must not land.
        stale.Decision = AgentRunDecision.Rejected;
        stale.ApprovedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleReader.SaveChangesAsync());

        await using var check = database.NewContext();
        Assert.Equal(AgentRunDecision.Approved, (await check.AgentRuns.SingleAsync()).Decision);
    }

    [PostgresFact]
    public async Task IfTheZoneRecomputeFails_TheWholeApprovalRollsBack()
    {
        await using var database = await PostgresDatabase.CreateAsync();
        await database.MigrateAsync();
        var (incidentId, runId) = await SeedProposalAsync(database);
        var queue = new RecordingQueue();

        await using (var db = database.NewContext())
        {
            await Assert.ThrowsAsync<IOException>(
                () => NewService(db, queue, new FailingZones()).ApproveAsync(runId, IncidentSeverity.Critical, Coordinator));
        }

        // The decision was saved inside the transaction before the failure; it must be gone.
        await using var check = database.NewContext();
        var run = await check.AgentRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        var incident = await check.Incidents.AsNoTracking().SingleAsync(i => i.Id == incidentId);

        Assert.Equal(AgentRunDecision.Pending, run.Decision);
        Assert.Null(run.ApprovedAt);
        Assert.Equal(IncidentSeverity.Moderate, incident.Severity);
        Assert.Equal(1000, incident.AffectedRadiusMeters);
        Assert.Empty(queue.Jobs);

        // And the run is still decidable once the zone service recovers.
        await using var retry = database.NewContext();
        await NewService(retry, queue).ApproveAsync(runId, null, Coordinator);
        Assert.Single(queue.Jobs);
    }
}
