using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentB.Services;

namespace RescueSriLanka.Api.Tests;

public class EmergencyContactServiceTests
{
    [Fact]
    public async Task Seeder_AddsTheNationalAndDistrictNumbersOnce()
    {
        await using var context = CreateContext();

        await EmergencyContactSeeder.SeedAsync(context);
        await EmergencyContactSeeder.SeedAsync(context);

        var contacts = await context.EmergencyContacts.ToListAsync();
        Assert.Equal(17, contacts.Count);
        Assert.Equal("1990", contacts.Single(c => c.Name == "Suwa Seriya Ambulance").PhoneNumber);
        Assert.Equal("118", contacts.Single(c => c.PhoneNumber == "119").SecondaryPhoneNumber);
        Assert.Equal(6, contacts.Count(c => c.Category == EmergencyContactCategory.DistrictDisaster));
    }

    [Fact]
    public async Task GetForAreaAsync_MatchesTheDistrictUnitNearestTheCoordinates()
    {
        await using var context = await SeededContextAsync();

        // Peradeniya is in Kandy district, nowhere near the other five units.
        var result = await new EmergencyContactService(context).GetForAreaAsync(7.2590, 80.5970, null);

        Assert.Equal("Kandy", result.MatchedDistrict);
        var local = Assert.Single(result.Contacts, c => c.District is not null);
        Assert.Equal("081-2202697", local.PhoneNumber);
        Assert.Equal(11, result.Contacts.Count(c => c.District is null));
    }

    [Fact]
    public async Task GetForAreaAsync_PutsTheLocalUnitFirst()
    {
        await using var context = await SeededContextAsync();

        var result = await new EmergencyContactService(context).GetForAreaAsync(6.93, 79.86, null);

        Assert.Equal("Colombo", result.Contacts[0].District);
    }

    [Fact]
    public async Task GetForAreaAsync_FallsBackToTheNamedDistrictWhenNoCoordinatesAreNear()
    {
        await using var context = await SeededContextAsync();

        // Jaffna has no unit within range, so the profile district is used instead.
        var result = await new EmergencyContactService(context).GetForAreaAsync(9.66, 80.02, "galle district");

        Assert.Equal("Galle", result.MatchedDistrict);
    }

    [Fact]
    public async Task GetForAreaAsync_ReturnsOnlyNationalNumbersWhenNothingMatches()
    {
        await using var context = await SeededContextAsync();

        var result = await new EmergencyContactService(context).GetForAreaAsync(9.66, 80.02, "Jaffna");

        Assert.Null(result.MatchedDistrict);
        Assert.All(result.Contacts, c => Assert.Null(c.District));
        Assert.Equal(11, result.Contacts.Count);
    }

    [Fact]
    public async Task GetForAreaAsync_SkipsInactiveContacts()
    {
        await using var context = await SeededContextAsync();
        var ambulance = await context.EmergencyContacts.SingleAsync(c => c.Name == "Suwa Seriya Ambulance");
        ambulance.IsActive = false;
        await context.SaveChangesAsync();

        var result = await new EmergencyContactService(context).GetForAreaAsync(null, null, null);

        Assert.DoesNotContain(result.Contacts, c => c.Name == "Suwa Seriya Ambulance");
    }

    private static async Task<AppDbContext> SeededContextAsync()
    {
        var context = CreateContext();
        await EmergencyContactSeeder.SeedAsync(context);
        return context;
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
