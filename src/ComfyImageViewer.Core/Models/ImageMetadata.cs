namespace ComfyImageViewer.Core.Models;

public sealed record ImageMetadata(
    MetadataStatus Status,
    IReadOnlyList<string> ModelNames,
    IReadOnlyList<string> PromptTexts,
    string RawPromptJson,
    string RawWorkflowJson);
