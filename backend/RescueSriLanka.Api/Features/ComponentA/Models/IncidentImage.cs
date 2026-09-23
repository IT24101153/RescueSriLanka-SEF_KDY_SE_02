using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Features.ComponentA.Models;
/// <summary>A photo attached to an incident, typically captured on the Flutter app.</summary>
public class IncidentImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    [MaxLength(500)]
    public required string StoragePath { get; set; }

    [MaxLength(200)]
    public string? FileName { get; set; }

    [MaxLength(100)]
    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    [MaxLength(300)]
    public string? Caption { get; set; }

    public Guid? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
