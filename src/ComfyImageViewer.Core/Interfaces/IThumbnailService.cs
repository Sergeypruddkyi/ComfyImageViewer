using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Interfaces;

public interface IThumbnailService
{
    Task<string?> GetThumbnailAsync(ImageEntry entry, int edgeSize, CancellationToken ct = default);
}
