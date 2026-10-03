using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace RescueSriLanka.Api.Services.Storage;

/// <summary>
/// The only photo storage: every incident, profile and help-request photo is
/// uploaded to Cloudinary, and the row stores the absolute secure URL, so
/// serving a photo costs the API nothing — the client fetches it from the CDN.
///
/// Configuration — never hard-code the secret:
///   "Cloudinary": { "CloudName": "...", "ApiKey": "...", "ApiSecret": "..." }
/// In deployment supply them as Cloudinary__CloudName / __ApiKey / __ApiSecret.
///
/// The API starts without these settings, so tests and a fresh clone still run.
/// An upload made without them fails with a clear error instead.
/// </summary>
public class CloudinaryImageStore(
    IOptions<CloudinaryOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<CloudinaryImageStore> logger) : IImageStore
{
    private Cloudinary? _cloudinary;

    public string Name => "cloudinary";

    public async Task<StoredImage> SaveAsync(
        Guid ownerId, string category, IFormFile file, CancellationToken ct = default)
    {
        var cloudinary = Client();

        await using var stream = file.OpenReadStream();

        var parameters = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            // One folder per owner keeps the media library navigable and makes
            // "delete everything for this incident/user" a single call later.
            Folder = $"rescuesrilanka/{category}/{ownerId}",
            UniqueFilename = true,
            Overwrite = false
        };

        var result = await cloudinary.UploadAsync(parameters, ct);

        if (result.Error is not null)
        {
            // Surfaced to the caller as a failure — never a silent success.
            throw new InvalidOperationException($"Cloudinary upload failed: {result.Error.Message}");
        }

        var url = result.SecureUrl?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Cloudinary returned no URL for the upload.");
        }

        logger.LogInformation(
            "Uploaded {Category} image {PublicId} for {OwnerId}", category, result.PublicId, ownerId);

        return new StoredImage(url, result.PublicId);
    }

    public async Task<byte[]?> ReadAsync(string location, CancellationToken ct = default)
    {
        // Photos stored before Cloudinary became the only backend hold site-relative
        // paths that no longer resolve to anything. Skip them rather than fail.
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            logger.LogWarning("Stored image is not a Cloudinary URL, so it cannot be read: {Location}", location);
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient(nameof(CloudinaryImageStore));
            return await client.GetByteArrayAsync(uri, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The agent can still reason from text and location, so a photo it
            // cannot fetch degrades the analysis rather than failing it.
            logger.LogWarning(ex, "Could not fetch image from Cloudinary: {Location}", location);
            return null;
        }
    }

    /// <summary>
    /// Built on first upload rather than at start-up, so an API without
    /// credentials still boots. The client-facing message names no settings;
    /// the log does.
    /// </summary>
    private Cloudinary Client()
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            logger.LogError(
                "Photo upload refused: Cloudinary settings are missing (CloudName, ApiKey or ApiSecret).");
            throw new InvalidOperationException("Photo storage is not configured on the server.");
        }

        return _cloudinary ??= new Cloudinary(
            new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret))
        {
            Api = { Secure = true }
        };
    }
}
