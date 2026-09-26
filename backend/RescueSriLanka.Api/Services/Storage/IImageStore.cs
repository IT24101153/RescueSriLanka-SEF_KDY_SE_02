namespace RescueSriLanka.Api.Services.Storage;

/// <summary>
/// Where an uploaded photo ended up. <paramref name="Location"/> is what gets
/// persisted on the IncidentImage row and handed back to the clients — either
/// an absolute https URL (Cloudinary) or a site-relative path (local disk).
/// </summary>
public record StoredImage(string Location, string? PublicId);

/// <summary>
/// The bytes half of image handling, split out from <see cref="IImageStorageService"/>
/// so the validation and database rules are written once and the destination can
/// change underneath them.
///
/// Two implementations ship: Cloudinary for anything deployed (a container's
/// disk does not survive a restart, so uploads written there disappear), and
/// local disk as the no-credentials fallback for development.
/// </summary>
public interface IImageStore
{
    /// <summary>Shown in logs and health output so the active backend is never a guess.</summary>
    string Name { get; }

    /// <summary>
    /// <paramref name="category"/> groups photos that belong together on disk or
    /// in Cloudinary (e.g. "incidents", "avatars") — <paramref name="ownerId"/> is
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
