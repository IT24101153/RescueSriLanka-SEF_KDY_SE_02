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
/// <see cref="IImageStore"/>'s business — Cloudinary — so the rules below hold
/// whichever store is registered.
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
        var contentType = Validate(file);

        var stored = await store.SaveAsync(incidentId, "incidents", file, ct);

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

    /// <summary>
    /// Throws <see cref="ArgumentException"/> for a file that would be refused.
    /// Public so a report filed together with its photo can be turned away
    /// before the report is created, rather than leaving a report behind whose
    /// photo was always going to fail.
    /// </summary>
    /// <returns>The content type, lower-cased.</returns>
    public static string Validate(IFormFile file)
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

        // The declared type is whatever the client says; the file's own header has to agree with it.
        using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (!HasImageSignature(header.AsSpan(0, read), contentType))
        {
            throw new ArgumentException("The file is not a valid image.");
        }

        return contentType;
    }

    private static bool HasImageSignature(ReadOnlySpan<byte> header, string contentType) => contentType switch
    {
        "image/jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        "image/png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
        "image/heic" => header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8),
        _ => false
    };

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
