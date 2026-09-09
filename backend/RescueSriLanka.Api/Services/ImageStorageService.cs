using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services;

public interface IImageStorageService
{
    Task<IncidentImage> SaveAsync(
        Guid incidentId, IFormFile file, string? caption, Guid? userId, CancellationToken ct = default);

    /// <summary>Reads image bytes back for the agent's vision step.</summary>
    Task<IReadOnlyList<(string MimeType, byte[] Data)>> LoadForAnalysisAsync(
        Guid incidentId, int maxImages, long maxTotalBytes, CancellationToken ct = default);
}

/// <summary>
/// Stores incident photos on disk under wwwroot so they can be served back by
/// URL, and reads them for the Incident Analysis Agent.
/// </summary>
public class ImageStorageService(
    AppDbContext db,
    IWebHostEnvironment environment,
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

        // Never trust the client's filename — generate our own.
        var extension = Path.GetExtension(file.FileName);
        extension = extension.Length is > 0 and <= 6 ? extension.ToLowerInvariant() : ".jpg";
        var storedName = $"{Guid.NewGuid():N}{extension}";

        var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var folder = Path.Combine(root, "uploads", "incidents", incidentId.ToString());
        Directory.CreateDirectory(folder);

        var fullPath = Path.Combine(folder, storedName);
        await using (var stream = File.Create(fullPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var image = new IncidentImage
        {
            IncidentId = incidentId,
            StoragePath = $"/uploads/incidents/{incidentId}/{storedName}",
            FileName = Path.GetFileName(file.FileName),
            ContentType = contentType,
            SizeBytes = file.Length,
            Caption = caption,
            UploadedByUserId = userId
        };

        db.IncidentImages.Add(image);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Stored image {Id} for incident {IncidentId}", image.Id, incidentId);
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

        var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var results = new List<(string, byte[])>();
        long total = 0;

        foreach (var record in records)
        {
            var relative = record.StoragePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(root, relative);

            if (!File.Exists(path))
            {
                logger.LogWarning("Image file missing on disk: {Path}", record.StoragePath);
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(path, ct);

            // Stop before the request grows large enough to be rejected.
            if (total + bytes.Length > maxTotalBytes) break;

            total += bytes.Length;
            results.Add((record.ContentType ?? "image/jpeg", bytes));
        }

        return results;
    }
}
