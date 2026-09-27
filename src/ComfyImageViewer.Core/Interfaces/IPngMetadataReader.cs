namespace ComfyImageViewer.Core.Interfaces;

public interface IPngMetadataReader
{
    IReadOnlyDictionary<string, string> ReadTextChunks(string pngPath);
}
