using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Tests;

public class ComponentDVehicleRegistrationTests
{
    private const string Duplicate = "A vehicle with this registration number already exists.";

    [Theory]
    [InlineData(false, "TEST-01")]
    [InlineData(true, "TEST-01")]
    [InlineData(false, "test-01")]
    [InlineData(false, "  TEST-01  ")]
    public async Task DuplicateCreateReturns409WithoutInserting(bool otherTeam, string plate)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        var teamId = fixture.TeamId;
        if (otherTeam) teamId = await factory.InDb(async db => {
            var team = new RescueTeam { Name = "Other" }; db.Add(team); await db.SaveChangesAsync(); return team.Id;
        });
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync($"/api/rescueteams/{teamId}/vehicles", new CreateVehicleDto(plate, VehicleType.Ambulance, 4));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(Duplicate, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await factory.InDb(db => db.Vehicles.CountAsync()));
    }

    [Fact]
    public async Task UniqueCreateNormalizesWithoutRemovingInternalSpacesOrPunctuation()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles", new CreateVehicleDto(" wp cab-1234 ", VehicleType.Ambulance, 4));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var vehicle = await factory.InDb(db => db.Vehicles.SingleAsync(v => v.Id != fixture.VehicleId));
        Assert.Equal("WP CAB-1234", vehicle.PlateNumber);
        Assert.Equal(4, vehicle.Capacity);
        var distinct = await client.PostAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles", new CreateVehicleDto("WP-CAB-1234", VehicleType.Ambulance, 4));
        Assert.Equal(HttpStatusCode.OK, distinct.StatusCode);
    }

    [Theory]
    [InlineData(" test-01 ", "TEST-01")]
    [InlineData(" kk 0999 ", "KK 0999")]
    public async Task UpdateAllowsOwnPlateAndPersistsNormalizedPlate(string input, string expected)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using var client = factory.Client();
        var response = await client.PutAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles/{fixture.VehicleId}", new UpdateVehicleDto(input, VehicleType.Ambulance, VehicleStatus.Available, 4));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, (await factory.InDb(db => db.Vehicles.SingleAsync())).PlateNumber);
    }

    [Fact]
    public async Task UpdateToAnotherVehiclesPlateReturns409AndPreservesData()
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        await factory.InDb(async db => { db.Add(new Vehicle { RescueTeamId = fixture.TeamId, PlateNumber = "OTHER", Capacity = 2 }); return await db.SaveChangesAsync(); });
        using var client = factory.Client();
        var response = await client.PutAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles/{fixture.VehicleId}", new UpdateVehicleDto(" other ", VehicleType.Boat, VehicleStatus.Available, 2));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(Duplicate, await response.Content.ReadAsStringAsync());
        var row = await factory.InDb(db => db.Vehicles.SingleAsync(v => v.Id == fixture.VehicleId));
        Assert.Equal("TEST-01", row.PlateNumber); Assert.Equal(4, row.Capacity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentPostgresUniqueViolationReturnsClean409(bool update)
    {
        var interceptor = new UniqueViolationInterceptor();
        using var factory = new ComponentDApiFactory { SaveInterceptor = interceptor };
        var fixture = await factory.Seed();
        interceptor.Enabled = true;
        using var client = factory.Client();
        var response = update
            ? await client.PutAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles/{fixture.VehicleId}", new UpdateVehicleDto("NEW", VehicleType.Ambulance, VehicleStatus.Available, 4))
            : await client.PostAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles", new CreateVehicleDto("NEW", VehicleType.Ambulance, 4));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(Duplicate, body); Assert.DoesNotContain("23505", body); Assert.DoesNotContain("IX_Vehicles", body);
        Assert.Equal("TEST-01", (await factory.InDb(db => db.Vehicles.SingleAsync())).PlateNumber);
    }

    [Fact]
    public async Task RelationalUniqueIndexRejectsDuplicateAcrossTeams()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RescueSriLanka.Api.Features.ComponentD.Data.ComponentDDbContext>().UseSqlite(connection).Options;
        await using var db = new RescueSriLanka.Api.Features.ComponentD.Data.ComponentDDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var first = new RescueTeam { Name = "First" };
        var second = new RescueTeam { Name = "Second" };
        db.AddRange(first, second);
        db.Add(new Vehicle { RescueTeamId = first.Id, PlateNumber = "WP CAB-1234", Capacity = 4 });
        await db.SaveChangesAsync();
        db.Add(new Vehicle { RescueTeamId = second.Id, PlateNumber = "WP CAB-1234", Capacity = 2 });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var sqlite = Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(error.InnerException);
        Assert.Equal(2067, sqlite.SqliteExtendedErrorCode); // SQLITE_CONSTRAINT_UNIQUE
    }

    [Theory]
    [InlineData("TEST-01", VehicleType.Ambulance, VehicleStatus.Available)]
    [InlineData("NEW-02", VehicleType.Boat, VehicleStatus.UnderMaintenance)]
    public async Task UpdatePersistsCapacityAndOtherFields(string plate, VehicleType type, VehicleStatus status)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using var client = factory.Client();
        var response = await client.PutAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles/{fixture.VehicleId}", new UpdateVehicleDto(plate, type, status, 7));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(7, result.RootElement.GetProperty("capacity").GetInt32());
        var row = await factory.InDb(db => db.Vehicles.SingleAsync(v => v.Id == fixture.VehicleId));
        Assert.Equal(7, row.Capacity);
        Assert.Equal(plate, row.PlateNumber);
        Assert.Equal(type, row.Type);
        Assert.Equal(status, row.Status);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("1.5")]
    public async Task InvalidUpdateCapacityReturns400WithoutSaving(string capacity)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed();
        using var client = factory.Client();
        using var body = new StringContent("{\"plateNumber\":\"TEST-01\",\"type\":\"Ambulance\",\"status\":\"Available\",\"capacity\":" + capacity + "}", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"/api/rescueteams/{fixture.TeamId}/vehicles/{fixture.VehicleId}", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(4, (await factory.InDb(db => db.Vehicles.SingleAsync())).Capacity);
    }

    private sealed class UniqueViolationInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new DbUpdateException("Simulated concurrent duplicate", new PostgresException("duplicate", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation, constraintName: "IX_Vehicles_PlateNumber"));
            return ValueTask.FromResult(result);
        }
    }
}
