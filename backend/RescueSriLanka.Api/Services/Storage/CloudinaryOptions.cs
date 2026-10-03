namespace RescueSriLanka.Api.Services.Storage;

/// <summary>
/// The "Cloudinary" configuration section. Every photo the API accepts is
/// uploaded there; nothing is kept on the API's own disk.
/// </summary>
public class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string? CloudName { get; set; }

    public string? ApiKey { get; set; }

    public string? ApiSecret { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret);
}
