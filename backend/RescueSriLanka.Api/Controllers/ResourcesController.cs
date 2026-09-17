using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Contracts;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Controllers;

[ApiController]
[Route("api/resources")]
public class ResourcesController(IResourceManagementService resourceService) : ControllerBase
{
    [HttpGet("shelters")]
    public async Task<IActionResult> GetShelters(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetSheltersAsync(cancellationToken));

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

    [HttpGet("medical-supplies")]
    public async Task<IActionResult> GetMedicalSupplies(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetMedicalSuppliesAsync(cancellationToken));

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

    [HttpGet("food-water-stock")]
    public async Task<IActionResult> GetFoodWaterStock(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetFoodWaterStockAsync(cancellationToken));

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

    [HttpGet("alerts/low-stock")]
    public async Task<IActionResult> GetLowStockAlerts(CancellationToken cancellationToken) =>
        Ok(await resourceService.GetLowStockAlertsAsync(cancellationToken));

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
}
