using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Controllers;

/// <summary>
/// The phone registers its push address here when push is turned on, and again
/// whenever Firebase rotates it. It removes the address on sign-out or when push
/// is turned off, so the phone stops receiving this account's notifications.
/// </summary>
[ApiController]
[Route("api/push/devices")]
[Authorize(Roles = nameof(UserRole.Citizen))]
public class PushDevicesController(AppDbContext db) : ControllerBase
{
    // PUT /api/push/devices
    [HttpPut]
    public async Task<IActionResult> Register([FromBody] RegisterPushDeviceRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var now = DateTime.UtcNow;
        var device = await db.DeviceTokens.FirstOrDefaultAsync(entry => entry.Token == request.Token, ct);

        if (device is null)
        {
            db.DeviceTokens.Add(new DeviceToken
            {
                UserId = userId,
                Token = request.Token,
                Platform = request.Platform,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            // A phone that changes hands belongs to whoever signed in on it last.
            device.UserId = userId;
            device.Platform = request.Platform;
            device.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // DELETE /api/push/devices?token=...
    [HttpDelete]
    public async Task<IActionResult> Unregister([FromQuery] string? token, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(token)) return BadRequest(new { message = "token is required." });

        // Only this account's own registration is removed. A token that belongs to someone else is left alone.
        var device = await db.DeviceTokens.FirstOrDefaultAsync(
            entry => entry.Token == token && entry.UserId == userId, ct);

        if (device is not null)
        {
            db.DeviceTokens.Remove(device);
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
