using AiCodeGraph.Core.Query;

namespace AiCodeGraph.Tests;

public class QueryPlanCacheTests
{
    [Fact]
    public void TryGet_CacheHit_ReturnsSamePlanForIdenticalQueries()
    {
        var cache = new QueryPlanCache();
        var plan = new QueryPlan
        {
            ResolvedSeeds = new List<string> { "A", "B" },
            Direction = ExpandDirection.Callees,
            CreatedAt = DateTime.UtcNow,
            QueryHash = "hash1"
        };

        cache.Set("hash1", plan);

        var found = cache.TryGet("hash1", out var retrieved);

        Assert.True(found);
        Assert.NotNull(retrieved);
        Assert.Equal(plan.ResolvedSeeds, retrieved.ResolvedSeeds);
        Assert.Equal(plan.Direction, retrieved.Direction);
    }

    [Fact]
    public void TryGet_CacheMiss_ReturnsFalseForDifferentQueries()
    {
        var cache = new QueryPlanCache();
        var plan = new QueryPlan
        {
            ResolvedSeeds = new List<string> { "A" },
            Direction = ExpandDirection.Both,
            CreatedAt = DateTime.UtcNow,
            QueryHash = "hash1"
        };

        cache.Set("hash1", plan);

        var found = cache.TryGet("hash2", out var retrieved);

        Assert.False(found);
        Assert.Null(retrieved);
    }

    [Fact]
    public void ComputeQueryHash_OutputChangesDoNotAffectHash()
    {
        var query1 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Output = new QueryOutput { MaxResults = 10 }
        };

        var query2 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Output = new QueryOutput { MaxResults = 100 }
        };

        var hash1 = QueryPlanCache.ComputeQueryHash(query1);
        var hash2 = QueryPlanCache.ComputeQueryHash(query2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeQueryHash_SeedChangesAffectHash()
    {
        var query1 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test1" }
        };

        var query2 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test2" }
        };

        var hash1 = QueryPlanCache.ComputeQueryHash(query1);
        var hash2 = QueryPlanCache.ComputeQueryHash(query2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeQueryHash_ExpandChangesAffectHash()
    {
        var query1 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Expand = new QueryExpand { Direction = ExpandDirection.Callers }
        };

        var query2 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Expand = new QueryExpand { Direction = ExpandDirection.Callees }
        };

        var hash1 = QueryPlanCache.ComputeQueryHash(query1);
        var hash2 = QueryPlanCache.ComputeQueryHash(query2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void TryGet_ExpiredEntry_EvictsAndReturnsFalse()
    {
        var cache = new QueryPlanCache(maxCacheSize: 10, cacheExpiration: TimeSpan.FromMilliseconds(1));
        var plan = new QueryPlan
        {
            ResolvedSeeds = new List<string> { "A" },
            Direction = ExpandDirection.Both,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10), // Already expired
            QueryHash = "hash1"
        };

        cache.Set("hash1", plan);

        // Wait for expiration to kick in
        Thread.Sleep(10);

        var found = cache.TryGet("hash1", out var retrieved);

        Assert.False(found);
        Assert.Null(retrieved);
    }

    [Fact]
    public void Set_OverMaxSize_EvictsOldEntries()
    {
        var cache = new QueryPlanCache(maxCacheSize: 2);

        for (int i = 0; i < 5; i++)
        {
            var plan = new QueryPlan
            {
                ResolvedSeeds = new List<string> { $"method{i}" },
                Direction = ExpandDirection.Both,
                CreatedAt = DateTime.UtcNow,
                QueryHash = $"hash{i}"
            };
            cache.Set($"hash{i}", plan);
        }

        var stats = cache.GetStats();

        // Cache should not exceed max size (may evict down to below max)
        Assert.True(stats.Size <= 2);
    }

    [Fact]
    public void Clear_EmptiesCache()
    {
        var cache = new QueryPlanCache();

        for (int i = 0; i < 3; i++)
        {
            var plan = new QueryPlan
            {
                ResolvedSeeds = new List<string> { $"method{i}" },
                Direction = ExpandDirection.Both,
                CreatedAt = DateTime.UtcNow,
                QueryHash = $"hash{i}"
            };
            cache.Set($"hash{i}", plan);
        }

        cache.Clear();
        var stats = cache.GetStats();

        Assert.Equal(0, stats.Size);
        Assert.Equal(0, stats.Hits);
        Assert.Equal(0, stats.Misses);
    }

    [Fact]
    public void GetStats_TracksHitsAndMisses()
    {
        var cache = new QueryPlanCache();
        var plan = new QueryPlan
        {
            ResolvedSeeds = new List<string> { "A" },
            Direction = ExpandDirection.Both,
            CreatedAt = DateTime.UtcNow,
            QueryHash = "hash1"
        };

        cache.Set("hash1", plan);

        // Generate hits
        cache.TryGet("hash1", out _);
        cache.TryGet("hash1", out _);

        // Generate misses
        cache.TryGet("nonexistent1", out _);
        cache.TryGet("nonexistent2", out _);
        cache.TryGet("nonexistent3", out _);

        var stats = cache.GetStats();

        Assert.Equal(2, stats.Hits);
        Assert.Equal(3, stats.Misses);
        Assert.Equal(1, stats.Size);
    }

    [Fact]
    public async Task TryGet_ConcurrentAccess_ThreadSafe()
    {
        var cache = new QueryPlanCache(maxCacheSize: 100);
        var tasks = new List<Task>();

        // Concurrent writes
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var plan = new QueryPlan
                {
                    ResolvedSeeds = new List<string> { $"method{index}" },
                    Direction = ExpandDirection.Both,
                    CreatedAt = DateTime.UtcNow,
                    QueryHash = $"hash{index}"
                };
                cache.Set($"hash{index}", plan);
            }));
        }

        // Concurrent reads
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                cache.TryGet($"hash{index}", out _);
            }));
        }

        await Task.WhenAll(tasks);

        // Should not throw and cache should be in valid state
        var stats = cache.GetStats();
        Assert.True(stats.Size >= 0);
        Assert.True(stats.Hits + stats.Misses >= 50);
    }

    [Fact]
    public void ComputeQueryHash_FilterChangesAffectHash()
    {
        var query1 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Filter = new QueryFilter { MinComplexity = 5 }
        };

        var query2 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Filter = new QueryFilter { MinComplexity = 10 }
        };

        var hash1 = QueryPlanCache.ComputeQueryHash(query1);
        var hash2 = QueryPlanCache.ComputeQueryHash(query2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeQueryHash_RankChangesAffectHash()
    {
        var query1 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Rank = new QueryRank { Strategy = RankStrategy.Complexity }
        };

        var query2 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Rank = new QueryRank { Strategy = RankStrategy.BlastRadius }
        };

        var hash1 = QueryPlanCache.ComputeQueryHash(query1);
        var hash2 = QueryPlanCache.ComputeQueryHash(query2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeQueryHash_IdenticalQueries_ProduceSameHash()
    {
        var query1 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test", MethodPattern = "*Get*" },
            Expand = new QueryExpand { Direction = ExpandDirection.Both, MaxDepth = 3 },
            Filter = new QueryFilter { MinComplexity = 5, ExcludeTests = true },
            Rank = new QueryRank { Strategy = RankStrategy.Combined, Descending = true }
        };

        var query2 = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test", MethodPattern = "*Get*" },
            Expand = new QueryExpand { Direction = ExpandDirection.Both, MaxDepth = 3 },
            Filter = new QueryFilter { MinComplexity = 5, ExcludeTests = true },
            Rank = new QueryRank { Strategy = RankStrategy.Combined, Descending = true }
        };

        var hash1 = QueryPlanCache.ComputeQueryHash(query1);
        var hash2 = QueryPlanCache.ComputeQueryHash(query2);

        Assert.Equal(hash1, hash2);
    }
}
