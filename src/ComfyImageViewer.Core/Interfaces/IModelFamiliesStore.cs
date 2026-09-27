namespace ComfyImageViewer.Core.Interfaces;

public interface IModelFamiliesStore : IModelFamiliesProvider
{
    void Add(string name, string patternsRaw);

    void Update(string originalName, string name, string patternsRaw);

    void Remove(string name);
}
