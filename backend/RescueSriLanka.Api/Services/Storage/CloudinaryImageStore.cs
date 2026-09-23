using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace RescueSriLanka.Api.Services.Storage;

/// <summary>
/// Cloudinary-backed photo storage.
///
/// Configuration — never hard-code the secret:
///   "Cloudinary": { "CloudName": "...", "ApiKey": "...", "ApiSecret": "..." }
/// In deployment supply them as Cloudinary__CloudName / __ApiKey / __ApiSecret.
///
/// The row stores the absolute secure URL, so serving a photo costs the API
/// nothing — the client fetches it straight from the CDN.
/// </summary>
public class CloudinaryImageStore(
    Cloudinary cloudinary,
    IHttpClientFactory httpClientFactory,
    ILogger<CloudinaryImageStore> logger) : IImageStore
{
    public string Name => "cloudinary";

    public async Task<StoredImage> SaveAsync(
        Guid incidentId, IFormFile file, CancellationToken ct = default)
    {
        await using var stream = file.OpenReadStream();

        var parameters = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            // One folder per incident keeps the media library navigable and
            // makes "delete everything for this incident" a single call later.
            Folder = $"rescuesrilanka/incidents/{incidentId}",
            UniqueFilename = true,
            Overwrite = false
        };

        var result = await cloudinary.UploadAsync(parameters, ct);

        if (result.Error is not null)
        {
            // Surfaced as a 400 by the controller — never a silent success.
            throw new InvalidOperationException($"Cloudinary upload failed: {result.Error.Message}");
        }

        var url = result.SecureUrl?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Cloudinary returned no URL for the upload.");
        }

        logger.LogInformation(
            "Uploaded image {PublicId} for incident {IncidentId}", result.PublicId, incidentId);

        return new StoredImage(url, result.PublicId);
    }

    public async Task<byte[]?> ReadAsync(string location, CancellationToken ct = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(nameof(CloudinaryImageStore));
            return await client.GetByteArrayAsync(location, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The agent can still reason from text and location, so a photo it
            // cannot fetch degrades the analysis rather than failing it.
            logger.LogWarning(ex, "Could not fetch image from Cloudinary: {Location}", location);
            return null;
        }
    }
}
