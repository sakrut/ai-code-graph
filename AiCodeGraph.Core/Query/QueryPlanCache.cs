using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiCodeGraph.Core.Query;

/// <summary>
/// A cached query plan containing resolved seeds and pre-built configuration.
/// </summary>
public record QueryPlan
{
    /// <summary>Resolved seed method IDs.</summary>
    public required List<string> ResolvedSeeds { get; init; }

    /// <summary>The direction for expansion.</summary>
    public required ExpandDirection Direction { get; init; }

    /// <summary>When this plan was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Unique hash identifying this query shape.</summary>
    public required string QueryHash { get; init; }
}

/// <summary>
/// Cache statistics.
/// </summary>
public record CacheStats(int Hits, int Misses, int Size);

/// <summary>
/// Thread-safe cache for query plans with LRU eviction and time-based expiration.
/// </summary>
public class QueryPlanCache
{
    private readonly ConcurrentDictionary<string, (QueryPlan Plan, DateTime LastAccess)> _cache = new();
    private readonly int _maxCacheSize;
    private readonly TimeSpan _cacheExpiration;
    private int _hits;
    private int _misses;
    private readonly object _evictionLock = new();

    public QueryPlanCache(int maxCacheSize = 100, TimeSpan? cacheExpiration = null)
    {
        _maxCacheSize = maxCacheSize;
        _cacheExpiration = cacheExpiration ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Tries to get a cached query plan by hash.
    /// </summary>
    public bool TryGet(string hash, out QueryPlan? plan)
    {
        if (_cache.TryGetValue(hash, out var entry))
        {
            // Check expiration
            if (DateTime.UtcNow - entry.Plan.CreatedAt > _cacheExpiration)
            {
                _cache.TryRemove(hash, out _);
                plan = null;
                Interlocked.Increment(ref _misses);
                return false;
            }

            // Update last access time for LRU
            _cache[hash] = (entry.Plan, DateTime.UtcNow);
            plan = entry.Plan;
            Interlocked.Increment(ref _hits);
            return true;
        }

        plan = null;
        Interlocked.Increment(ref _misses);
        return false;
    }

    /// <summary>
    /// Caches a query plan.
    /// </summary>
    public void Set(string hash, QueryPlan plan)
    {
        // Check if we need to evict entries
        if (_cache.Count >= _maxCacheSize)
        {
            EvictOldEntries();
        }

        _cache[hash] = (plan, DateTime.UtcNow);
    }

    /// <summary>
    /// Clears all cached entries.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public CacheStats GetStats() => new(
        Interlocked.CompareExchange(ref _hits, 0, 0),
        Interlocked.CompareExchange(ref _misses, 0, 0),
        _cache.Count);

    private void EvictOldEntries()
    {
        lock (_evictionLock)
        {
            // Remove expired entries first
            var now = DateTime.UtcNow;
            var expiredKeys = _cache
                .Where(kv => now - kv.Value.Plan.CreatedAt > _cacheExpiration)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }

            // If still over limit, remove LRU entries
            while (_cache.Count >= _maxCacheSize)
            {
                var oldest = _cache
                    .OrderBy(kv => kv.Value.LastAccess)
                    .FirstOrDefault();

                if (oldest.Key != null)
                {
                    _cache.TryRemove(oldest.Key, out _);
                }
                else
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Computes a deterministic hash for a GraphQuery.
    /// Excludes Output settings since same query with different output = same plan.
    /// </summary>
    public static string ComputeQueryHash(GraphQuery query)
    {
        var hashInput = new StringBuilder();

        // Seed properties
        hashInput.Append("seed:");
        hashInput.Append(query.Seed.MethodId ?? "");
        hashInput.Append('|');
        hashInput.Append(query.Seed.MethodPattern ?? "");
        hashInput.Append('|');
        hashInput.Append(query.Seed.Namespace ?? "");
        hashInput.Append('|');
        hashInput.Append(query.Seed.Cluster ?? "");

        // Expand properties
        var expand = query.Expand ?? new QueryExpand();
        hashInput.Append("|expand:");
        hashInput.Append((int)expand.Direction);
        hashInput.Append('|');
        hashInput.Append(expand.MaxDepth);
        hashInput.Append('|');
        hashInput.Append(expand.IncludeTransitive);

        // Filter properties
        if (query.Filter != null)
        {
            hashInput.Append("|filter:");
            hashInput.Append(JsonSerializer.Serialize(query.Filter.IncludeNamespaces ?? new List<string>()));
            hashInput.Append('|');
            hashInput.Append(JsonSerializer.Serialize(query.Filter.ExcludeNamespaces ?? new List<string>()));
            hashInput.Append('|');
            hashInput.Append(JsonSerializer.Serialize(query.Filter.IncludeTypes ?? new List<string>()));
            hashInput.Append('|');
            hashInput.Append(query.Filter.MinComplexity ?? -1);
            hashInput.Append('|');
            hashInput.Append(query.Filter.MaxComplexity ?? -1);
            hashInput.Append('|');
            hashInput.Append(query.Filter.ExcludeTests);
        }

        // Rank properties
        var rank = query.Rank ?? new QueryRank();
        hashInput.Append("|rank:");
        hashInput.Append((int)rank.Strategy);
        hashInput.Append('|');
        hashInput.Append(rank.Descending);

        // Compute SHA256 hash
        var inputBytes = Encoding.UTF8.GetBytes(hashInput.ToString());
        var hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes);
    }
}
