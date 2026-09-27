using ComfyImageViewer.Core.Models;
using Xunit;

namespace ComfyImageViewer.Core.Tests;

public class ContractSmokeTests
{
    [Fact]
    public void Contracts_Instantiate()
    {
        var now = DateTime.UtcNow;
        var entry = new ImageEntry(@"C:\archive\a.png", "a.png", 123, now, now);
        var meta = new ImageMetadata(MetadataStatus.None, Array.Empty<string>(), Array.Empty<string>(), "", "");
        var folder = new FolderNode(@"C:\archive\sub", "sub");

        Assert.Equal("a.png", entry.Name);
        Assert.Equal(MetadataStatus.None, meta.Status);
        Assert.Equal("sub", folder.Name);
    }
}
