using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Interfaces;

public interface IArchiveScanner
{
    IAsyncEnumerable<FolderNode> ListSubfoldersAsync(string path, CancellationToken ct = default);
    IAsyncEnumerable<ImageEntry> ListImagesAsync(string path, CancellationToken ct = default);
}
