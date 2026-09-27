using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Interfaces;

public interface IComfyMetadataParser
{
    ImageMetadata Parse(IReadOnlyDictionary<string, string> textChunks);
}
