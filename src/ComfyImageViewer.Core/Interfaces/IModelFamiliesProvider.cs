using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Interfaces;

public interface IModelFamiliesProvider
{
    IReadOnlyList<ModelFamily> Families { get; }
}
