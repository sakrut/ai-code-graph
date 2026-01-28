using AiCodeGraph.Core.Embeddings;

namespace AiCodeGraph.Cli.Helpers;

/// <summary>
/// Cache for vector indices to avoid rebuilding them on every query.
/// </summary>
public static class VectorIndexCache
{
    private static readonly Dictionary<string, VectorIndex> _cache = new();
    private static readonly object _lock = new();

    public static VectorIndex GetOrBuild(string dbPath, List<(string MethodId, float[] Vector)> embeddings)
    {
        var key = Path.GetFullPath(dbPath);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var index = new VectorIndex();
            index.BuildIndex(embeddings);
            _cache[key] = index;
            return index;
        }
    }

    public static void Invalidate(string dbPath)
    {
        var key = Path.GetFullPath(dbPath);
        lock (_lock)
        {
            _cache.Remove(key);
        }
    }
}
