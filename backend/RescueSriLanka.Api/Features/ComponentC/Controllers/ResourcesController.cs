using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Features.ComponentC.DTOs;
using RescueSriLanka.Api.Features.ComponentC.Services;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Features.ComponentC.Controllers;

[ApiController]
[Route("api/resources")]
public class ResourcesController(IResourceManagementService resourceService) : ControllerBase
{
    // Changing stock, shelters and allocations is staff work. Reads stay open,
    // as do the citizen-facing resource request and donation posts, which the
    // Flutter app sends without an account.
    private const string Managers =
        nameof(UserRole.ResourceManager) + "," + nameof(UserRole.EmergencyCoordinator);

    [HttpGet("shelters")]
    public async Task<IActionResult> GetShelters(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetSheltersAsync(cancellationToken));

    [Authorize(Roles = Managers)]
    [HttpPost("shelters")]
    public async Task<IActionResult> CreateShelter(
        CreateShelterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var shelter = await resourceService.CreateShelterAsync(request, cancellationToken);
            return Created($"api/resources/shelters/{shelter.Id}", shelter);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Roles = Managers)]
    [HttpPut("shelters/{id:guid}")]
    public async Task<IActionResult> UpdateShelter(Guid id, UpdateShelterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var shelter = await resourceService.UpdateShelterAsync(id, request, cancellationToken);
            return shelter is null ? NotFound(new { error = "Shelter was not found." }) : Ok(shelter);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [Authorize(Roles = Managers)]
    [HttpDelete("shelters/{id:guid}")]
    public async Task<IActionResult> DeleteShelter(Guid id, CancellationToken cancellationToken) =>
        await resourceService.DeleteShelterAsync(id, cancellationToken) ? NoContent() : NotFound(new { error = "Shelter was not found." });

    [HttpGet("medical-supplies")]
    public async Task<IActionResult> GetMedicalSupplies(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetMedicalSuppliesAsync(cancellationToken));

    [Authorize(Roles = Managers)]
    [HttpPost("medical-supplies")]
    public async Task<IActionResult> CreateMedicalSupply(
        CreateMedicalSupplyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var supply = await resourceService.CreateMedicalSupplyAsync(request, cancellationToken);
            return Created($"api/resources/medical-supplies/{supply.Id}", supply);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Roles = Managers)]
    [HttpPut("medical-supplies/{id:guid}")]
    public async Task<IActionResult> UpdateMedicalSupply(Guid id, UpdateMedicalSupplyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var supply = await resourceService.UpdateMedicalSupplyAsync(id, request, cancellationToken);
            return supply is null ? NotFound(new { error = "Medical supply was not found." }) : Ok(supply);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [Authorize(Roles = Managers)]
    [HttpDelete("medical-supplies/{id:guid}")]
    public async Task<IActionResult> DeleteMedicalSupply(Guid id, CancellationToken cancellationToken) =>
        await resourceService.DeleteMedicalSupplyAsync(id, cancellationToken) ? NoContent() : NotFound(new { error = "Medical supply was not found." });

    [HttpGet("food-water-stock")]
    public async Task<IActionResult> GetFoodWaterStock(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetFoodWaterStockAsync(cancellationToken));

    [Authorize(Roles = Managers)]
    [HttpPost("food-water-stock")]
    public async Task<IActionResult> CreateFoodWaterStock(
        CreateFoodWaterStockRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var stock = await resourceService.CreateFoodWaterStockAsync(request, cancellationToken);
            return Created($"api/resources/food-water-stock/{stock.Id}", stock);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Roles = Managers)]
    [HttpPut("food-water-stock/{id:guid}")]
    public async Task<IActionResult> UpdateFoodWaterStock(Guid id, UpdateFoodWaterStockRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var stock = await resourceService.UpdateFoodWaterStockAsync(id, request, cancellationToken);
            return stock is null ? NotFound(new { error = "Food/water stock was not found." }) : Ok(stock);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [Authorize(Roles = Managers)]
    [HttpDelete("food-water-stock/{id:guid}")]
    public async Task<IActionResult> DeleteFoodWaterStock(Guid id, CancellationToken cancellationToken) =>
        await resourceService.DeleteFoodWaterStockAsync(id, cancellationToken) ? NoContent() : NotFound(new { error = "Food/water stock was not found." });

    [HttpGet("alerts/low-stock")]
    public async Task<IActionResult> GetLowStockAlerts(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetLowStockAlertsAsync(cancellationToken));

    [Authorize(Roles = Managers)]
    [HttpPost("allocations")]
    public async Task<IActionResult> Allocate(
        AllocateResourceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var allocation = await resourceService.AllocateAsync(request, cancellationToken);
            return Created($"api/resources/allocations/{allocation.Id}", allocation);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [Authorize(Roles = Managers)]
    [HttpPost("allocations/match")]
    public async Task<IActionResult> MatchAndAllocate(
        MatchResourceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var allocation = await resourceService.MatchAndAllocateAsync(request, cancellationToken);
            return Created($"api/resources/allocations/{allocation.Id}", allocation);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [Authorize(Roles = Managers)]
    [HttpPost("allocations/{allocationId:guid}/release")]
    public async Task<IActionResult> Release(
        Guid allocationId,
        CancellationToken cancellationToken)
    {
        var allocation = await resourceService.ReleaseAsync(allocationId, cancellationToken);
        return allocation is null
            ? NotFound(new { error = "Active allocation was not found." })
            : Ok(allocation);
    }

    [HttpGet("help-requests")]
    public async Task<IActionResult> GetHelpRequests(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetHelpRequestsAsync(cancellationToken));

    [HttpPost("help-requests")]
    public async Task<IActionResult> CreateHelpRequest(
        CreateHelpRequestRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var helpRequest = await resourceService.CreateHelpRequestAsync(request, cancellationToken);
            return Created($"api/resources/help-requests/{helpRequest.Id}", helpRequest);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("donations")]
    public async Task<IActionResult> GetDonations(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetDonationsAsync(cancellationToken));

    [HttpPost("donations")]
    public async Task<IActionResult> CreateDonation(
        CreateDonationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var donation = await resourceService.CreateDonationAsync(request, cancellationToken);
            return Created($"api/resources/donations/{donation.Id}", donation);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
