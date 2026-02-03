using System.Diagnostics;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core.Query;

/// <summary>
/// Result of executing a GraphQuery.
/// </summary>
public record QueryResult
{
    /// <summary>Whether the query executed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Error message if the query failed.</summary>
    public string? Error { get; init; }

    /// <summary>Resulting method nodes.</summary>
    public List<QueryResultNode> Nodes { get; init; } = new();

    /// <summary>Total matches before MaxResults limit was applied.</summary>
    public int TotalMatches { get; init; }

    /// <summary>Time taken to execute the query.</summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static QueryResult Ok(List<QueryResultNode> nodes, int totalMatches, TimeSpan executionTime) =>
        new() { Success = true, Nodes = nodes, TotalMatches = totalMatches, ExecutionTime = executionTime };

    /// <summary>Creates a failed result with an error message.</summary>
    public static QueryResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// A single node in the query result.
/// </summary>
public record QueryResultNode
{
    /// <summary>The method's unique identifier.</summary>
    public required string MethodId { get; init; }

    /// <summary>The method's fully qualified name.</summary>
    public required string FullName { get; init; }

    /// <summary>Distance from the seed (0 for seed nodes).</summary>
    public int Depth { get; init; }

    /// <summary>Ranking score if ranking was applied.</summary>
    public float? RankScore { get; init; }

    /// <summary>Cognitive complexity (if IncludeMetrics is true).</summary>
    public int? Complexity { get; init; }

    /// <summary>Lines of code (if IncludeMetrics is true).</summary>
    public int? Loc { get; init; }

    /// <summary>Nesting depth (if IncludeMetrics is true).</summary>
    public int? Nesting { get; init; }

    /// <summary>Source file path (if IncludeLocation is true).</summary>
    public string? FilePath { get; init; }

    /// <summary>Line number in source file (if IncludeLocation is true).</summary>
    public int? Line { get; init; }
}

/// <summary>
/// Executes GraphQuery objects by translating them to TraversalConfig,
/// running via GraphTraversalEngine, and formatting results.
/// </summary>
public class GraphQueryExecutor
{
    private readonly IStorageService _storage;
    private readonly IGraphTraversalEngine _traversalEngine;
    private readonly GraphQueryValidator _validator = new();
    private readonly QueryPlanCache _planCache;

    public GraphQueryExecutor(IStorageService storage, IGraphTraversalEngine traversalEngine)
        : this(storage, traversalEngine, new QueryPlanCache())
    {
    }

    public GraphQueryExecutor(IStorageService storage, IGraphTraversalEngine traversalEngine, QueryPlanCache cache)
    {
        _storage = storage;
        _traversalEngine = traversalEngine;
        _planCache = cache;
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public CacheStats GetCacheStats() => _planCache.GetStats();

    /// <summary>
    /// Clears the query plan cache.
    /// </summary>
    public void ClearCache() => _planCache.Clear();

    /// <summary>
    /// Executes a GraphQuery and returns formatted results.
    /// </summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="useCache">Whether to use plan caching. Default is true.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<QueryResult> ExecuteAsync(GraphQuery query, bool useCache = true, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Validate query
        var validation = _validator.Validate(query);
        if (!validation.IsValid)
            return QueryResult.Fail($"Query validation failed: {string.Join("; ", validation.Errors)}");

        try
        {
            List<string> seedMethodIds;
            ExpandDirection direction;

            // Try to use cached plan
            var queryHash = QueryPlanCache.ComputeQueryHash(query);
            if (useCache && _planCache.TryGet(queryHash, out var cachedPlan) && cachedPlan != null)
            {
                seedMethodIds = cachedPlan.ResolvedSeeds;
                direction = cachedPlan.Direction;
            }
            else
            {
                // Resolve seeds to method IDs
                seedMethodIds = await ResolveSeedsAsync(query.Seed, ct);
                if (seedMethodIds.Count == 0)
                    return QueryResult.Fail("No methods matched the seed criteria");

                direction = (query.Expand ?? new QueryExpand()).Direction;

                // Cache the plan if caching is enabled
                if (useCache)
                {
                    var plan = new QueryPlan
                    {
                        ResolvedSeeds = seedMethodIds,
                        Direction = direction,
                        CreatedAt = DateTime.UtcNow,
                        QueryHash = queryHash
                    };
                    _planCache.Set(queryHash, plan);
                }
            }

            if (seedMethodIds.Count == 0)
                return QueryResult.Fail("No methods matched the seed criteria");

            // Execute traversals for each seed
            var allNodes = new List<QueryResultNode>();
            var totalMatches = 0;
            var seenMethodIds = new HashSet<string>();

            // If Direction is None, just return the seed methods without traversal
            if (direction == ExpandDirection.None)
            {
                foreach (var seedId in seedMethodIds)
                {
                    ct.ThrowIfCancellationRequested();
                    if (seenMethodIds.Add(seedId))
                    {
                        var info = await _storage.GetMethodInfoAsync(seedId, ct);
                        if (info.HasValue)
                        {
                            var resultNode = await CreateSeedResultNodeAsync(seedId, info.Value.FullName, query, ct);
                            allNodes.Add(resultNode);
                        }
                    }
                }
                totalMatches = allNodes.Count;
            }
            else
            {
                foreach (var seedId in seedMethodIds)
                {
                    ct.ThrowIfCancellationRequested();

                    var config = TranslateToTraversalConfig(query, seedId);
                    var result = await _traversalEngine.TraverseAsync(config, ct);

                    foreach (var node in result.Nodes)
                    {
                        if (seenMethodIds.Add(node.MethodId))
                        {
                            var resultNode = await CreateResultNodeAsync(node, query, ct);
                            allNodes.Add(resultNode);
                        }
                    }

                    totalMatches += result.TotalNodesVisited;
                }
            }

            // Apply ranking across all results
            await ApplyRankingAsync(allNodes, query.Rank, ct);

            // Apply output limits
            var output = query.Output ?? new QueryOutput();
            var limitedNodes = allNodes.Take(output.MaxResults).ToList();

            stopwatch.Stop();
            return QueryResult.Ok(limitedNodes, totalMatches, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return QueryResult.Fail($"Query execution failed: {ex.Message}");
        }
    }

    private async Task<List<string>> ResolveSeedsAsync(QuerySeed seed, CancellationToken ct)
    {
        var methodIds = new List<string>();

        // MethodId - direct lookup
        if (seed.MethodId != null)
        {
            var info = await _storage.GetMethodInfoAsync(seed.MethodId, ct);
            if (info.HasValue)
                methodIds.Add(seed.MethodId);
        }

        // MethodPattern - fuzzy search (convert wildcards to SQL LIKE pattern)
        if (seed.MethodPattern != null)
        {
            // Convert * to SQL LIKE % and ? to _
            var sqlPattern = seed.MethodPattern.Replace("*", "").Replace("?", "_");
            var matches = await _storage.SearchMethodsAsync(sqlPattern, ct);
            methodIds.AddRange(matches.Select(m => m.Id));
        }

        // Namespace - query methods by namespace prefix
        if (seed.Namespace != null)
        {
            var matches = await _storage.SearchMethodsAsync(seed.Namespace, ct);
            methodIds.AddRange(matches.Select(m => m.Id));
        }

        // Cluster - use cluster query (if supported)
        if (seed.Cluster != null)
        {
            var clusters = await _storage.GetClustersAsync(ct);
            var matchingCluster = clusters.FirstOrDefault(c =>
                c.Label.Contains(seed.Cluster, StringComparison.OrdinalIgnoreCase));
            if (matchingCluster != null)
                methodIds.AddRange(matchingCluster.MethodIds);
        }

        return methodIds.Distinct().ToList();
    }

    private async Task<QueryResultNode> CreateSeedResultNodeAsync(
        string methodId,
        string fullName,
        GraphQuery query,
        CancellationToken ct)
    {
        var output = query.Output ?? new QueryOutput();

        int? complexity = null, loc = null, nesting = null;
        string? filePath = null;
        int? line = null;

        if (output.IncludeMetrics)
        {
            var metrics = await _storage.GetMethodMetricsAsync(methodId, ct);
            if (metrics.HasValue)
            {
                complexity = metrics.Value.CognitiveComplexity;
                loc = metrics.Value.LinesOfCode;
                nesting = metrics.Value.NestingDepth;
            }
        }

        if (output.IncludeLocation)
        {
            var info = await _storage.GetMethodInfoAsync(methodId, ct);
            if (info.HasValue)
            {
                filePath = info.Value.FilePath;
                line = info.Value.StartLine;
            }
        }

        return new QueryResultNode
        {
            MethodId = methodId,
            FullName = fullName,
            Depth = 0,
            RankScore = null,
            Complexity = complexity,
            Loc = loc,
            Nesting = nesting,
            FilePath = filePath,
            Line = line
        };
    }

    private static TraversalConfig TranslateToTraversalConfig(GraphQuery query, string seedMethodId)
    {
        var expand = query.Expand ?? new QueryExpand();
        var rank = query.Rank ?? new QueryRank();

        var direction = expand.Direction switch
        {
            ExpandDirection.None => TraversalDirection.Both, // No expansion handled via MaxDepth=0
            ExpandDirection.Callers => TraversalDirection.Callers,
            ExpandDirection.Callees => TraversalDirection.Callees,
            ExpandDirection.Both => TraversalDirection.Both,
            _ => TraversalDirection.Both
        };

        var ranking = rank.Strategy switch
        {
            RankStrategy.BlastRadius => RankingStrategy.BlastRadius,
            RankStrategy.Complexity => RankingStrategy.Complexity,
            RankStrategy.Coupling => RankingStrategy.Coupling,
            RankStrategy.Combined => RankingStrategy.Combined,
            _ => RankingStrategy.BlastRadius
        };

        var filter = TranslateToFilterConfig(query.Filter);

        // If direction is None, set depth to 0 to return only the seed
        var maxDepth = expand.Direction == ExpandDirection.None ? 1 : Math.Max(1, expand.MaxDepth);

        return new TraversalConfig(
            SeedMethodId: seedMethodId,
            Direction: direction,
            MaxDepth: maxDepth,
            Strategy: TraversalStrategy.BFS,
            Ranking: ranking,
            MaxResults: null, // We apply limits after merging all results
            Filter: filter);
    }

    private static FilterConfig? TranslateToFilterConfig(QueryFilter? filter)
    {
        if (filter == null)
            return null;

        return new FilterConfig(
            IncludeNamespaces: filter.IncludeNamespaces?.ToArray(),
            ExcludeNamespaces: filter.ExcludeNamespaces?.ToArray(),
            IncludeTypes: filter.IncludeTypes?.ToArray(),
            ExcludeTypes: null,
            IncludeAccessibility: null,
            ExcludeGeneratedCode: true);
    }

    private async Task<QueryResultNode> CreateResultNodeAsync(
        TraversalNode node,
        GraphQuery query,
        CancellationToken ct)
    {
        var output = query.Output ?? new QueryOutput();

        int? complexity = null, loc = null, nesting = null;
        string? filePath = null;
        int? line = null;

        if (output.IncludeMetrics)
        {
            var metrics = await _storage.GetMethodMetricsAsync(node.MethodId, ct);
            if (metrics.HasValue)
            {
                complexity = metrics.Value.CognitiveComplexity;
                loc = metrics.Value.LinesOfCode;
                nesting = metrics.Value.NestingDepth;
            }
        }

        if (output.IncludeLocation)
        {
            var info = await _storage.GetMethodInfoAsync(node.MethodId, ct);
            if (info.HasValue)
            {
                filePath = info.Value.FilePath;
                line = info.Value.StartLine;
            }
        }

        return new QueryResultNode
        {
            MethodId = node.MethodId,
            FullName = node.FullName,
            Depth = node.Depth,
            RankScore = node.RankingScore > 0 ? node.RankingScore : null,
            Complexity = complexity,
            Loc = loc,
            Nesting = nesting,
            FilePath = filePath,
            Line = line
        };
    }

    private async Task ApplyRankingAsync(List<QueryResultNode> nodes, QueryRank? rank, CancellationToken ct)
    {
        if (nodes.Count <= 1)
            return;

        rank ??= new QueryRank();

        // Re-compute ranking scores based on strategy using indexed for loop
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            float score = 0;

            switch (rank.Strategy)
            {
                case RankStrategy.Complexity:
                    if (node.Complexity.HasValue)
                    {
                        score = node.Complexity.Value;
                    }
                    else
                    {
                        var metrics = await _storage.GetMethodMetricsAsync(node.MethodId, ct);
                        score = metrics?.CognitiveComplexity ?? 0;
                    }
                    break;

                case RankStrategy.BlastRadius:
                    var brMetrics = await _storage.GetMethodMetricsAsync(node.MethodId, ct);
                    score = brMetrics?.BlastRadius ?? 0;
                    break;

                case RankStrategy.Coupling:
                    var callers = await _storage.GetCallersAsync(node.MethodId, ct);
                    var callees = await _storage.GetCalleesAsync(node.MethodId, ct);
                    score = callers.Count + callees.Count;
                    break;

                case RankStrategy.Combined:
                    var combMetrics = await _storage.GetMethodMetricsAsync(node.MethodId, ct);
                    var complexity = combMetrics?.CognitiveComplexity ?? 0;
                    var blastRadius = combMetrics?.BlastRadius ?? 0;
                    score = (float)(complexity * (1 + Math.Log(blastRadius + 1)));
                    break;
            }

            nodes[i] = node with { RankScore = score };
        }

        // Sort by rank score
        if (rank.Descending)
            nodes.Sort((a, b) => (b.RankScore ?? 0).CompareTo(a.RankScore ?? 0));
        else
            nodes.Sort((a, b) => (a.RankScore ?? 0).CompareTo(b.RankScore ?? 0));
    }
}
