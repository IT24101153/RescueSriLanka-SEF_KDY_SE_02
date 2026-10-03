namespace RescueSriLanka.Api.Services.Storage;

/// <summary>
/// Where an uploaded photo ended up. <paramref name="Location"/> is what gets
/// persisted on the row and handed back to the clients — an absolute https URL
/// on Cloudinary.
/// </summary>
public record StoredImage(string Location, string? PublicId);

/// <summary>
/// The bytes half of image handling, split out from <see cref="IImageStorageService"/>
/// so the validation and database rules are written once and the destination can
/// change underneath them.
///
/// Cloudinary is the only implementation. Photos are never written to the API's
/// own disk: a container's disk does not survive a restart, and serving them from
/// the API would make every image request pass through it.
/// </summary>
public interface IImageStore
{
    /// <summary>Shown in logs so the active backend is never a guess.</summary>
    string Name { get; }

    /// <summary>
    /// <paramref name="category"/> groups photos that belong together in Cloudinary
    /// (e.g. "incidents", "avatars", "helprequests") — <paramref name="ownerId"/> is
    /// the incident or user the photo is for.
    /// </summary>
    Task<StoredImage> SaveAsync(
        Guid ownerId, string category, IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Reads the bytes back for the Incident Analysis Agent's vision step.
    /// Null when the image can no longer be fetched — a missing photo degrades
    /// the analysis, it never fails the request.
    /// </summary>
    Task<byte[]?> ReadAsync(string location, CancellationToken ct = default);
}
