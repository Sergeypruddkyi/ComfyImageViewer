using Xunit;

namespace ComfyImageViewer.Core.Tests.Scanning;

/// <summary>Хелперы для материализации IAsyncEnumerable без сторонних пакетов (System.Linq.Async запрещён).</summary>
internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source, CancellationToken ct = default)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
        {
            list.Add(item);
        }

        return list;
    }
}
