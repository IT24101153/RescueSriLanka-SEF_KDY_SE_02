using Microsoft.AspNetCore.Http;
using RescueSriLanka.Api.Services.Storage;

namespace RescueSriLanka.Api.Tests;

/// <summary>Stands in for Cloudinary in tests that never upload a photo.</summary>
public sealed class NoOpImageStore : IImageStore
{
    public string Name => "test";

    public Task<StoredImage> SaveAsync(
        Guid ownerId, string category, IFormFile file, CancellationToken ct = default) =>
        Task.FromResult(new StoredImage("https://cdn.test/none.jpg", "none"));

    public Task<byte[]?> ReadAsync(string location, CancellationToken ct = default) =>
        Task.FromResult<byte[]?>(null);
}
