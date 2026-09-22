namespace RescueSriLanka.Api.Services.Storage;

/// <summary>
/// Development fallback: writes under wwwroot/uploads so UseStaticFiles can
/// serve the photo straight back. Fine on a laptop, wrong for deployment —
/// the files vanish with the container, which is why Cloudinary exists.
/// </summary>
public class LocalDiskImageStore(
    IWebHostEnvironment environment,
    ILogger<LocalDiskImageStore> logger) : IImageStore
{
    public string Name => "local-disk";

    public async Task<StoredImage> SaveAsync(
        Guid incidentId, IFormFile file, CancellationToken ct = default)
    {
        // Never trust the client's filename — generate our own.
        var extension = Path.GetExtension(file.FileName);
        extension = extension.Length is > 0 and <= 6 ? extension.ToLowerInvariant() : ".jpg";
        var storedName = $"{Guid.NewGuid():N}{extension}";

        var folder = Path.Combine(WebRoot, "uploads", "incidents", incidentId.ToString());
        Directory.CreateDirectory(folder);

        await using (var stream = File.Create(Path.Combine(folder, storedName)))
        {
            await file.CopyToAsync(stream, ct);
        }

        logger.LogInformation("Stored image on disk for incident {IncidentId}", incidentId);
        return new StoredImage($"/uploads/incidents/{incidentId}/{storedName}", null);
    }

    public async Task<byte[]?> ReadAsync(string location, CancellationToken ct = default)
    {
        var relative = location.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(WebRoot, relative);

        if (!File.Exists(path))
        {
            logger.LogWarning("Image file missing on disk: {Location}", location);
            return null;
        }

        return await File.ReadAllBytesAsync(path, ct);
    }

    private string WebRoot =>
        environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
}
