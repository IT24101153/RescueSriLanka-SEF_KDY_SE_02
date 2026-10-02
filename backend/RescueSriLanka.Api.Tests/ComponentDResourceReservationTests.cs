using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Features.ComponentD.DTOs;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Tests;

public class ComponentDResourceReservationTests
{
    [Theory]
    [InlineData(DispatchStatus.Resolved)]
    [InlineData(DispatchStatus.Cancelled)]
    public async Task TerminalHistoryAllowsCapacityRoundTripWithoutChangingHistory(DispatchStatus status)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(dispatch: true);
        await Configure(factory, fixture, status);
        var before = await History(factory);
        using var client = factory.Client();
        foreach (var capacity in new[] { 2, 1 })
        {
            var response = await client.PutAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles/{fixture.VehicleId}",
                new UpdateVehicleDto("LU 3454", VehicleType.Truck, VehicleStatus.Available, capacity));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(capacity, await factory.InDb(db => db.Vehicles.Where(v => v.Id == fixture.VehicleId).Select(v => v.Capacity).SingleAsync()));
            Assert.Equal(before, await History(factory));
        }
        await factory.InDb(async db => { db.Vehicles.Add(new Vehicle { RescueTeamId = fixture.TeamId, PlateNumber = "OTHER", Capacity = 1 }); return await db.SaveChangesAsync(); });
        var duplicate = await client.PutAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles/{fixture.VehicleId}",
            new UpdateVehicleDto(" other ", VehicleType.Truck, VehicleStatus.Available, 2));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("registration number already exists", await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(1, await factory.InDb(db => db.Vehicles.Where(v => v.Id == fixture.VehicleId).Select(v => v.Capacity).SingleAsync()));
        Assert.Equal(before, await History(factory));
    }

    [Theory]
    [InlineData(DispatchStatus.Pending)]
    [InlineData(DispatchStatus.Dispatched)]
    [InlineData(DispatchStatus.EnRoute)]
    [InlineData(DispatchStatus.OnScene)]
    public async Task ActiveDispatchBlocksEvenWhenResourcesAreMarkedAvailable(DispatchStatus status)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(dispatch: true);
        await Configure(factory, fixture, status);
        var before = await History(factory);
        using var client = factory.Client();
        var response = await client.PutAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles/{fixture.VehicleId}",
            new UpdateVehicleDto("LU 3454", VehicleType.Truck, VehicleStatus.Available, 2));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("active assignment or mission", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await factory.InDb(db => db.Vehicles.Select(v => v.Capacity).SingleAsync()));
        Assert.Equal(before, await History(factory));
    }

    [Theory]
    [InlineData(AssignmentStatus.Proposed, HttpStatusCode.Conflict)]
    [InlineData(AssignmentStatus.PendingApproval, HttpStatusCode.Conflict)]
    [InlineData(AssignmentStatus.Approved, HttpStatusCode.Conflict)]
    [InlineData(AssignmentStatus.Rejected, HttpStatusCode.OK)]
    public async Task NoDispatchPreservesPlanningRules(AssignmentStatus status, HttpStatusCode expected)
    {
        using var factory = new ComponentDApiFactory();
        var fixture = await factory.Seed(assignment: true);
        await factory.InDb(async db => { (await db.Assignments.SingleAsync()).Status = status; return await db.SaveChangesAsync(); });
        using var client = factory.Client();
        var response = await client.PutAsJsonAsync($"/api/rescueteams/{fixture.TeamId}/vehicles/{fixture.VehicleId}",
            new UpdateVehicleDto("TEST-01", VehicleType.Ambulance, VehicleStatus.Available, 2));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 2 : 4, await factory.InDb(db => db.Vehicles.Select(v => v.Capacity).SingleAsync()));
    }

    private static Task<int> Configure(ComponentDApiFactory factory, ComponentDApiFactory.Fixture fixture, DispatchStatus status) => factory.InDb(async db =>
    {
        (await db.RescueTeams.SingleAsync()).Status = TeamStatus.Available;
        var vehicle = await db.Vehicles.SingleAsync();
        vehicle.Status = VehicleStatus.Available; vehicle.Capacity = 1; vehicle.PlateNumber = "LU 3454"; vehicle.Type = VehicleType.Truck;
        var dispatch = await db.Dispatches.SingleAsync();
        dispatch.Status = status;
        if (status == DispatchStatus.Resolved) dispatch.ResolvedAt = DateTime.UtcNow;
        if (status == DispatchStatus.Cancelled) dispatch.CancelledAt = DateTime.UtcNow;
        return await db.SaveChangesAsync();
    });

    private static Task<string> History(ComponentDApiFactory factory) => factory.InDb(async db =>
    {
        // Snapshot every mapped scalar, including audit values, without navigation cycles.
        var assignment = await db.Assignments.SingleAsync();
        var dispatch = await db.Dispatches.SingleAsync();
        return JsonSerializer.Serialize(new[] {
            db.Entry(assignment).Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue),
            db.Entry(dispatch).Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue),
        });
    });
}
