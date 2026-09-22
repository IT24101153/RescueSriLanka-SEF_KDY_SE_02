using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Services.Storage;
using RescueSriLanka.Api.Features.ComponentA.Models;

namespace RescueSriLanka.Api.Features.ComponentA.Services;
public interface IImageStorageService
{
    Task<IncidentImage> SaveAsync(
        Guid incidentId, IFormFile file, string? caption, Guid? userId, CancellationToken ct = default);

    /// <summary>Reads image bytes back for the agent's vision step.</summary>
    Task<IReadOnlyList<(string MimeType, byte[] Data)>> LoadForAnalysisAsync(
        Guid incidentId, int maxImages, long maxTotalBytes, CancellationToken ct = default);
}

/// <summary>
/// Validation and bookkeeping for incident photos. Where the bytes go is the
/// <see cref="IImageStore"/>'s business — Cloudinary when it is configured,
/// local disk otherwise — so the rules below hold whichever backend is active.
/// </summary>
public class ImageStorageService(
    AppDbContext db,
    IImageStore store,
    ILogger<ImageStorageService> logger) : IImageStorageService
{
    private static readonly string[] AllowedTypes =
        ["image/jpeg", "image/png", "image/webp", "image/heic"];

    private const long MaxFileBytes = 8 * 1024 * 1024;

    public async Task<IncidentImage> SaveAsync(
        Guid incidentId, IFormFile file, string? caption, Guid? userId,
        CancellationToken ct = default)
    {
        if (file.Length == 0)
        {
            throw new ArgumentException("The uploaded file is empty.");
        }

        if (file.Length > MaxFileBytes)
        {
            throw new ArgumentException($"Images must be {MaxFileBytes / 1024 / 1024} MB or smaller.");
        }

        var contentType = file.ContentType.ToLowerInvariant();
        if (!AllowedTypes.Contains(contentType))
        {
            throw new ArgumentException(
                $"Unsupported image type '{file.ContentType}'. Allowed: {string.Join(", ", AllowedTypes)}.");
        }

        var stored = await store.SaveAsync(incidentId, file, ct);

        var image = new IncidentImage
        {
            IncidentId = incidentId,
            StoragePath = stored.Location,
            FileName = Path.GetFileName(file.FileName),
            ContentType = contentType,
            SizeBytes = file.Length,
            Caption = caption,
            UploadedByUserId = userId
        };

        db.IncidentImages.Add(image);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Stored image {Id} for incident {IncidentId} via {Store}",
            image.Id, incidentId, store.Name);

        return image;
    }

    public async Task<IReadOnlyList<(string MimeType, byte[] Data)>> LoadForAnalysisAsync(
        Guid incidentId, int maxImages, long maxTotalBytes, CancellationToken ct = default)
    {
        var records = await db.IncidentImages
            .AsNoTracking()
            .Where(image => image.IncidentId == incidentId)
            .OrderBy(image => image.UploadedAt)
            .Take(Math.Clamp(maxImages, 1, 8))
            .ToListAsync(ct);

        var results = new List<(string, byte[])>();
        long total = 0;

        foreach (var record in records)
        {
            var bytes = await store.ReadAsync(record.StoragePath, ct);
            if (bytes is null) continue;

            // Stop before the request grows large enough to be rejected.
            if (total + bytes.Length > maxTotalBytes) break;

            total += bytes.Length;
            results.Add((record.ContentType ?? "image/jpeg", bytes));
        }

        return results;
    }
}
