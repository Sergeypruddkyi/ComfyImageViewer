namespace ComfyImageViewer.Core.Models;

public sealed record ImageEntry(string FullPath, string Name, long SizeBytes, DateTime CreatedUtc, DateTime ModifiedUtc);
