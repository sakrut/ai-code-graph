using System.Diagnostics;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core.Query;

public class GraphTraversalEngine : IGraphTraversalEngine
{
    private readonly IStorageService _storage;

    // Session-level caches (cleared per traversal)
    private Dictionary<string, int> _blastRadiusCache = new();
    private Dictionary<string, List<string>> _callersCache = new();
    private Dictionary<string, List<string>> _calleesCache = new();
    private Dictionary<string, (int Complexity, int Loc, int Nesting)?> _metricsCache = new();

    public GraphTraversalEngine(IStorageService storage)
    {
        _storage = storage;
    }

    public async Task<TraversalResult> TraverseAsync(TraversalConfig config, CancellationToken ct = default)
    {
        config.Validate();
        ClearCaches();

        var stopwatch = Stopwatch.StartNew();

        // Validate seed method exists
        var seedInfo = await _storage.GetMethodInfoAsync(config.SeedMethodId, ct);
        if (seedInfo == null)
            return TraversalResult.Empty(config.SeedMethodId, config);

        // Execute traversal
        var (nodes, edges, totalVisited) = config.Strategy switch
        {
            TraversalStrategy.BFS => await TraverseBfsAsync(config, seedInfo.Value, ct),
            TraversalStrategy.DFS => await TraverseDfsAsync(config, seedInfo.Value, ct),
            _ => throw new ArgumentException($"Unknown traversal strategy: {config.Strategy}")
        };

        // Apply filters
        if (config.Filter != null)
            ApplyFilters(nodes, edges, config.Filter);

        // Rank nodes by configured strategy
        await RankNodesAsync(nodes, config.Ranking, ct);

        stopwatch.Stop();

        return new TraversalResult(
            config.SeedMethodId,
            seedInfo.Value.FullName,
            nodes,
            edges,
            totalVisited,
            stopwatch.ElapsedMilliseconds,
            config);
    }

    private async Task<(List<TraversalNode> Nodes, List<TraversalEdge> Edges, int TotalVisited)> TraverseBfsAsync(
        TraversalConfig config,
        (string Id, string Name, string FullName, string? FilePath, int StartLine) seedInfo,
        CancellationToken ct)
    {
        var visited = new HashSet<string> { config.SeedMethodId };
        var nodes = new List<TraversalNode>();
        var edges = new List<TraversalEdge>();
        var queue = new Queue<(string Id, int Depth, TraversalDirection FromDirection)>();

        // Add seed node
        nodes.Add(new TraversalNode(
            config.SeedMethodId,
            seedInfo.FullName,
            0,
            TraversalDirection.Both));

        queue.Enqueue((config.SeedMethodId, 0, TraversalDirection.Both));

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var (currentId, currentDepth, fromDirection) = queue.Dequeue();

            if (currentDepth >= config.MaxDepth)
                continue;

            // Check max results (early termination)
            if (config.MaxResults.HasValue && nodes.Count >= config.MaxResults.Value)
                break;

            // Explore callees
            if (config.Direction is TraversalDirection.Callees or TraversalDirection.Both)
            {
                var callees = await GetCalleesAsync(currentId, ct);
                foreach (var calleeId in callees)
                {
                    edges.Add(new TraversalEdge(currentId, calleeId, TraversalDirection.Callees));

                    if (visited.Add(calleeId))
                    {
                        // Check max results before adding
                        if (config.MaxResults.HasValue && nodes.Count >= config.MaxResults.Value)
                            break;

                        var info = await _storage.GetMethodInfoAsync(calleeId, ct);
                        nodes.Add(new TraversalNode(
                            calleeId,
                            info?.FullName ?? calleeId,
                            currentDepth + 1,
                            TraversalDirection.Callees));

                        queue.Enqueue((calleeId, currentDepth + 1, TraversalDirection.Callees));
                    }
                }
            }

            // Check if we hit max results in callees loop
            if (config.MaxResults.HasValue && nodes.Count >= config.MaxResults.Value)
                break;

            // Explore callers
            if (config.Direction is TraversalDirection.Callers or TraversalDirection.Both)
            {
                var callers = await GetCallersAsync(currentId, ct);
                foreach (var callerId in callers)
                {
                    edges.Add(new TraversalEdge(callerId, currentId, TraversalDirection.Callers));

                    if (visited.Add(callerId))
                    {
                        // Check max results before adding
                        if (config.MaxResults.HasValue && nodes.Count >= config.MaxResults.Value)
                            break;

                        var info = await _storage.GetMethodInfoAsync(callerId, ct);
                        nodes.Add(new TraversalNode(
                            callerId,
                            info?.FullName ?? callerId,
                            currentDepth + 1,
                            TraversalDirection.Callers));

                        queue.Enqueue((callerId, currentDepth + 1, TraversalDirection.Callers));
                    }
                }
            }
        }

        return (nodes, edges, visited.Count);
    }

    private async Task<(List<TraversalNode> Nodes, List<TraversalEdge> Edges, int TotalVisited)> TraverseDfsAsync(
        TraversalConfig config,
        (string Id, string Name, string FullName, string? FilePath, int StartLine) seedInfo,
        CancellationToken ct)
    {
        var visited = new HashSet<string> { config.SeedMethodId };
        var nodes = new List<TraversalNode>();
        var edges = new List<TraversalEdge>();
        var stack = new Stack<(string Id, int Depth, TraversalDirection FromDirection)>();

        // Add seed node
        nodes.Add(new TraversalNode(
            config.SeedMethodId,
            seedInfo.FullName,
            0,
            TraversalDirection.Both));

        stack.Push((config.SeedMethodId, 0, TraversalDirection.Both));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var (currentId, currentDepth, fromDirection) = stack.Pop();

            if (currentDepth >= config.MaxDepth)
                continue;

            // Check max results (early termination)
            if (config.MaxResults.HasValue && nodes.Count >= config.MaxResults.Value)
                break;

            // For DFS, we push in reverse order so that the first neighbor is explored first
            var neighborsToExplore = new List<(string Id, TraversalDirection Dir)>();

            // Collect callees
            if (config.Direction is TraversalDirection.Callees or TraversalDirection.Both)
            {
                var callees = await GetCalleesAsync(currentId, ct);
                foreach (var calleeId in callees)
                {
                    edges.Add(new TraversalEdge(currentId, calleeId, TraversalDirection.Callees));
                    if (!visited.Contains(calleeId))
                        neighborsToExplore.Add((calleeId, TraversalDirection.Callees));
                }
            }

            // Collect callers
            if (config.Direction is TraversalDirection.Callers or TraversalDirection.Both)
            {
                var callers = await GetCallersAsync(currentId, ct);
                foreach (var callerId in callers)
                {
                    edges.Add(new TraversalEdge(callerId, currentId, TraversalDirection.Callers));
                    if (!visited.Contains(callerId))
                        neighborsToExplore.Add((callerId, TraversalDirection.Callers));
                }
            }

            // Push in reverse order for correct DFS ordering
            neighborsToExplore.Reverse();
            foreach (var (neighborId, dir) in neighborsToExplore)
            {
                if (visited.Add(neighborId))
                {
                    // Check max results before adding
                    if (config.MaxResults.HasValue && nodes.Count >= config.MaxResults.Value)
                        break;

                    var info = await _storage.GetMethodInfoAsync(neighborId, ct);
                    nodes.Add(new TraversalNode(
                        neighborId,
                        info?.FullName ?? neighborId,
                        currentDepth + 1,
                        dir));

                    stack.Push((neighborId, currentDepth + 1, dir));
                }
            }
        }

        return (nodes, edges, visited.Count);
    }

    private void ApplyFilters(List<TraversalNode> nodes, List<TraversalEdge> edges, FilterConfig filter)
    {
        // Keep seed node (depth 0) regardless of filter
        var nodesToRemove = nodes
            .Where(n => n.Depth > 0 && !filter.Matches(n.FullName, null))
            .Select(n => n.MethodId)
            .ToHashSet();

        nodes.RemoveAll(n => nodesToRemove.Contains(n.MethodId));
        edges.RemoveAll(e => nodesToRemove.Contains(e.FromMethodId) || nodesToRemove.Contains(e.ToMethodId));
    }

    private void ClearCaches()
    {
        _blastRadiusCache.Clear();
        _callersCache.Clear();
        _calleesCache.Clear();
        _metricsCache.Clear();
    }

    private async Task<List<string>> GetCallersAsync(string methodId, CancellationToken ct)
    {
        if (_callersCache.TryGetValue(methodId, out var cached))
            return cached;

        var callers = await _storage.GetCallersAsync(methodId, ct);
        _callersCache[methodId] = callers;
        return callers;
    }

    private async Task<List<string>> GetCalleesAsync(string methodId, CancellationToken ct)
    {
        if (_calleesCache.TryGetValue(methodId, out var cached))
            return cached;

        var callees = await _storage.GetCalleesAsync(methodId, ct);
        _calleesCache[methodId] = callees;
        return callees;
    }

    // Public methods for ranking strategies (implemented in subtasks 74.3-74.5)
    internal async Task<int> ComputeBlastRadiusAsync(string methodId, CancellationToken ct)
    {
        if (_blastRadiusCache.TryGetValue(methodId, out var cached))
            return cached;

        var affected = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(methodId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var callers = await GetCallersAsync(current, ct);
            foreach (var caller in callers)
            {
                if (affected.Add(caller))
                    queue.Enqueue(caller);
            }
        }

        var radius = affected.Count;
        _blastRadiusCache[methodId] = radius;
        return radius;
    }

    // Ranking dispatcher
    private async Task RankNodesAsync(List<TraversalNode> nodes, RankingStrategy strategy, CancellationToken ct)
    {
        if (nodes.Count <= 1) return; // Nothing to rank

        switch (strategy)
        {
            case RankingStrategy.BlastRadius:
                await RankByBlastRadiusAsync(nodes, ct);
                break;
            case RankingStrategy.Complexity:
                await RankByComplexityAsync(nodes, ct);
                break;
            case RankingStrategy.Coupling:
                await RankByCouplingAsync(nodes, ct);
                break;
            case RankingStrategy.Combined:
                await RankByCombinedAsync(nodes, new CombinedRankingWeights(), ct);
                break;
        }
    }

    private async Task RankByBlastRadiusAsync(List<TraversalNode> nodes, CancellationToken ct)
    {
        foreach (var node in nodes)
        {
            var radius = await ComputeBlastRadiusAsync(node.MethodId, ct);
            node.RankingScore = radius;
            node.Metrics = (node.Metrics ?? new TraversalNodeMetrics()) with { };
        }

        // Sort descending by blast radius (higher = more impactful)
        nodes.Sort((a, b) => b.RankingScore.CompareTo(a.RankingScore));
    }

    private async Task RankByComplexityAsync(List<TraversalNode> nodes, CancellationToken ct)
    {
        foreach (var node in nodes)
        {
            var metrics = await GetMethodMetricsAsync(node.MethodId, ct);
            var complexity = metrics?.Complexity ?? 0;
            var loc = metrics?.Loc ?? 0;

            node.RankingScore = complexity;
            node.Metrics = new TraversalNodeMetrics(
                CognitiveComplexity: complexity,
                LinesOfCode: loc);
        }

        // Sort descending by complexity
        nodes.Sort((a, b) => b.RankingScore.CompareTo(a.RankingScore));
    }

    private async Task RankByCouplingAsync(List<TraversalNode> nodes, CancellationToken ct)
    {
        foreach (var node in nodes)
        {
            var callers = await GetCallersAsync(node.MethodId, ct);
            var callees = await GetCalleesAsync(node.MethodId, ct);
            var ca = callers.Count;
            var ce = callees.Count;

            // Total coupling as score
            node.RankingScore = ca + ce;
            node.Metrics = new TraversalNodeMetrics(
                AfferentCoupling: ca,
                EfferentCoupling: ce);
        }

        // Sort descending by coupling
        nodes.Sort((a, b) => b.RankingScore.CompareTo(a.RankingScore));
    }

    private async Task RankByCombinedAsync(List<TraversalNode> nodes, CombinedRankingWeights weights, CancellationToken ct)
    {
        // First compute raw scores for all nodes
        var blastRadii = new Dictionary<string, int>();
        var complexities = new Dictionary<string, int>();
        var couplings = new Dictionary<string, int>();
        var locs = new Dictionary<string, int>();
        var afferent = new Dictionary<string, int>();
        var efferent = new Dictionary<string, int>();

        foreach (var node in nodes)
        {
            blastRadii[node.MethodId] = await ComputeBlastRadiusAsync(node.MethodId, ct);

            var metrics = await GetMethodMetricsAsync(node.MethodId, ct);
            complexities[node.MethodId] = metrics?.Complexity ?? 0;
            locs[node.MethodId] = metrics?.Loc ?? 0;

            var callers = await GetCallersAsync(node.MethodId, ct);
            var callees = await GetCalleesAsync(node.MethodId, ct);
            afferent[node.MethodId] = callers.Count;
            efferent[node.MethodId] = callees.Count;
            couplings[node.MethodId] = callers.Count + callees.Count;
        }

        // Normalize to 0-1 range
        var maxBR = blastRadii.Values.DefaultIfEmpty(0).Max();
        var maxCC = complexities.Values.DefaultIfEmpty(0).Max();
        var maxCoup = couplings.Values.DefaultIfEmpty(0).Max();

        foreach (var node in nodes)
        {
            var brNorm = maxBR > 0 ? (float)blastRadii[node.MethodId] / maxBR : 0;
            var ccNorm = maxCC > 0 ? (float)complexities[node.MethodId] / maxCC : 0;
            var coupNorm = maxCoup > 0 ? (float)couplings[node.MethodId] / maxCoup : 0;

            node.RankingScore = brNorm * weights.BlastRadiusWeight +
                               ccNorm * weights.ComplexityWeight +
                               coupNorm * weights.CouplingWeight;

            node.Metrics = new TraversalNodeMetrics(
                CognitiveComplexity: complexities[node.MethodId],
                LinesOfCode: locs[node.MethodId],
                AfferentCoupling: afferent[node.MethodId],
                EfferentCoupling: efferent[node.MethodId]);
        }

        // Sort descending by combined score
        nodes.Sort((a, b) => b.RankingScore.CompareTo(a.RankingScore));
    }

    private async Task<(int Complexity, int Loc, int Nesting)?> GetMethodMetricsAsync(string methodId, CancellationToken ct)
    {
        if (_metricsCache.TryGetValue(methodId, out var cached))
            return cached;

        var metrics = await _storage.GetMethodMetricsAsync(methodId, ct);
        var result = metrics.HasValue
            ? (metrics.Value.CognitiveComplexity, metrics.Value.LinesOfCode, metrics.Value.NestingDepth)
            : ((int, int, int)?)null;

        _metricsCache[methodId] = result;
        return result;
    }
}
