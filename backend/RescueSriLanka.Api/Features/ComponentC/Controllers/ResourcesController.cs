using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RescueSriLanka.Api.Features.ComponentC.DTOs;
using RescueSriLanka.Api.Features.ComponentC.Services;
using RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Features.ComponentC.Controllers;

[ApiController]
[Route("api/resources")]
public class ResourcesController(
    IResourceManagementService resourceService,
    IResourceAllocationAgent allocationAgent,
    IResourceForecastAgent forecastAgent,
    IActionEmailService emails) : ControllerBase
{
    // Changing stock and allocations is staff work. Inventory reads are open,
    // while request and donation history requires an account and is ownership-scoped.
    private const string Managers =
        nameof(UserRole.ResourceManager) + "," + nameof(UserRole.EmergencyCoordinator);

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

    [HttpGet("managed-supplies")]
    public async Task<IActionResult> GetManagedSupplies(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetManagedSuppliesAsync(cancellationToken));

    [Authorize(Roles = Managers)]
    [HttpPost("managed-supplies")]
    public async Task<IActionResult> CreateManagedSupply(
        CreateManagedSupplyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var supply = await resourceService.CreateManagedSupplyAsync(request, cancellationToken);
            return Created($"api/resources/managed-supplies/{supply.Id}", supply);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Roles = Managers)]
    [HttpPut("managed-supplies/{id:guid}")]
    public async Task<IActionResult> UpdateManagedSupply(
        Guid id,
        CreateManagedSupplyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var supply = await resourceService.UpdateManagedSupplyAsync(id, request, cancellationToken);
            return supply is null ? NotFound(new { error = "Supply was not found." }) : Ok(supply);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Roles = Managers)]
    [HttpDelete("managed-supplies/{id:guid}")]
    public async Task<IActionResult> DeleteManagedSupply(Guid id, CancellationToken cancellationToken) =>
        await resourceService.DeleteManagedSupplyAsync(id, cancellationToken)
            ? NoContent()
            : NotFound(new { error = "Supply was not found." });

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
    [HttpPost("help-requests/{id:guid}/allocation-recommendation")]
    public async Task<IActionResult> RecommendAllocation(Guid id, CancellationToken cancellationToken) =>
        Ok(await allocationAgent.RecommendAsync(id, cancellationToken));

    [Authorize(Roles = Managers)]
    [HttpPost("help-requests/allocation-plan")]
    public async Task<IActionResult> PlanAllocations(CancellationToken cancellationToken) =>
        Ok(await allocationAgent.PlanAsync(cancellationToken));

    [Authorize(Roles = Managers)]
    [HttpGet("stock-forecast")]
    public async Task<IActionResult> GetStockForecast(CancellationToken cancellationToken) =>
        Ok(await forecastAgent.ForecastAsync(cancellationToken));

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

    [Authorize]
    [HttpGet("help-requests")]
    public async Task<IActionResult> GetHelpRequests(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        var includeAll = User.IsInRole(nameof(UserRole.ResourceManager)) ||
            User.IsInRole(nameof(UserRole.EmergencyCoordinator));
        return Ok(await resourceService.GetHelpRequestsAsync(userId, includeAll, cancellationToken));
    }

    [HttpPost("help-requests")]
    [EnableRateLimiting("public")]
    public async Task<IActionResult> CreateHelpRequest(
        CreateHelpRequestRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
                ? parsedUserId
                : (Guid?)null;
            var helpRequest = await resourceService.CreateHelpRequestAsync(request, userId, cancellationToken);
            await emails.ResourceRequestSubmittedAsync(helpRequest.Id, cancellationToken);
            return Created($"api/resources/help-requests/{helpRequest.Id}", helpRequest);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Roles = nameof(UserRole.Citizen))]
    [HttpPost("help-requests/batch")]
    public async Task<IActionResult> CreateHelpRequestsBatch(
        CreateHelpRequestsBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var requests = await resourceService.CreateHelpRequestsBatchAsync(request, userId, cancellationToken);
            foreach (var item in requests)
                await emails.ResourceRequestSubmittedAsync(item.Id, cancellationToken);
            return Ok(requests);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Roles = Managers)]
    [HttpPatch("help-requests/{id:guid}/status")]
    public async Task<IActionResult> UpdateHelpRequestStatus(
        Guid id,
        UpdateHelpRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var helpRequest = await resourceService.UpdateHelpRequestStatusAsync(id, request, cancellationToken);
            if (helpRequest is null)
                return NotFound(new { error = "Help request was not found." });
            await emails.ResourceRequestStatusChangedAsync(id, request.Status, cancellationToken);
            return Ok(helpRequest);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    [Authorize]
    [HttpGet("donations")]
    public async Task<IActionResult> GetDonations(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        var includeAll = User.IsInRole(nameof(UserRole.ResourceManager)) ||
            User.IsInRole(nameof(UserRole.EmergencyCoordinator));
        return Ok(await resourceService.GetDonationsAsync(userId, includeAll, cancellationToken));
    }

    [HttpGet("donated-supplies")]
    public async Task<IActionResult> GetDonatedSupplies(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetDonatedSuppliesAsync(cancellationToken));

    [HttpPost("donations")]
    [EnableRateLimiting("public")]
    public async Task<IActionResult> CreateDonation(
        CreateDonationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
                ? parsedUserId
                : (Guid?)null;
            var donation = await resourceService.CreateDonationAsync(request, userId, cancellationToken);
            await emails.DonationSubmittedAsync(donation.Id, cancellationToken);
            return Created($"api/resources/donations/{donation.Id}", donation);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Roles = nameof(UserRole.Citizen))]
    [HttpPost("donations/batch")]
    public async Task<IActionResult> CreateDonationsBatch(
        CreateDonationsBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var donations = await resourceService.CreateDonationsBatchAsync(request, userId, cancellationToken);
            foreach (var item in donations)
                await emails.DonationSubmittedAsync(item.Id, cancellationToken);
            return Ok(donations);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [Authorize(Roles = Managers)]
    [HttpPatch("donations/{id:guid}/status")]
    public async Task<IActionResult> UpdateDonationStatus(
        Guid id,
        UpdateHelpRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var donation = await resourceService.UpdateDonationStatusAsync(id, request, cancellationToken);
            if (donation is null)
                return NotFound(new { error = "Donation was not found." });
            await emails.DonationStatusChangedAsync(id, request.Status, cancellationToken);
            return Ok(donation);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    [Authorize(Roles = Managers)]
    [HttpPatch("donations/batch/{submissionId:guid}/status")]
    public async Task<IActionResult> UpdateDonationBatchStatus(
        Guid submissionId,
        UpdateHelpRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var donations = await resourceService.UpdateDonationBatchStatusAsync(
                submissionId, request, cancellationToken);
            if (donations is null)
                return NotFound(new { error = "Donation submission was not found." });
            foreach (var item in donations)
                await emails.DonationStatusChangedAsync(item.Id, request.Status, cancellationToken);
            return Ok(donations);
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

}
