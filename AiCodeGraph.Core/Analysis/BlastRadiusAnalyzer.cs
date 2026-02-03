using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core.Analysis;

public record BlastRadiusInfo(
    int DirectCallers,
    int TransitiveCallers,
    int Depth,
    List<string> EntryPoints);

public class BlastRadiusAnalyzer
{
    /// <summary>
    /// Computes blast radius (transitive impact count) for all methods in the database.
    /// Uses BFS on the reverse call graph to count how many methods transitively call each method.
    /// </summary>
    public async Task<Dictionary<string, BlastRadiusInfo>> ComputeBlastRadiusAsync(
        IStorageService storage,
        int maxEntryPoints = 5,
        CancellationToken ct = default)
    {
        var methods = await storage.GetMethodsForExportAsync(null, ct).ConfigureAwait(false);
        if (methods.Count == 0)
            return new Dictionary<string, BlastRadiusInfo>();

        var allMethodIds = methods.Select(m => m.Id).ToHashSet();
        var allCalls = await storage.GetCallGraphForMethodsAsync(allMethodIds, ct).ConfigureAwait(false);

        // Build forward and reverse call graphs
        var forwardGraph = new Dictionary<string, HashSet<string>>(); // caller → callees
        var reverseGraph = new Dictionary<string, HashSet<string>>(); // callee → callers

        foreach (var methodId in allMethodIds)
        {
            forwardGraph[methodId] = new HashSet<string>();
            reverseGraph[methodId] = new HashSet<string>();
        }

        foreach (var (callerId, calleeId) in allCalls)
        {
            if (!forwardGraph.ContainsKey(callerId) || !reverseGraph.ContainsKey(calleeId))
                continue;

            forwardGraph[callerId].Add(calleeId);
            reverseGraph[calleeId].Add(callerId);
        }

        // Identify entry points (methods with no callers)
        var entryPoints = allMethodIds
            .Where(id => reverseGraph[id].Count == 0)
            .ToHashSet();

        // Compute depths from entry points using BFS
        var depthFromEntryPoint = ComputeDepths(forwardGraph, entryPoints);

        // Build method name lookup for entry point names
        var methodNames = methods.ToDictionary(m => m.Id, m => m.FullName);

        // Compute blast radius for each method
        var results = new Dictionary<string, BlastRadiusInfo>();

        foreach (var methodId in allMethodIds)
        {
            var directCallers = reverseGraph[methodId].Count;
            var (transitiveCallers, reachableEntryPoints) = ComputeTransitiveCallersAndEntryPoints(
                methodId, reverseGraph, entryPoints, maxEntryPoints);
            var depth = depthFromEntryPoint.GetValueOrDefault(methodId, 0);

            // Convert entry point IDs to names
            var entryPointNames = reachableEntryPoints
                .Select(id => methodNames.GetValueOrDefault(id, id))
                .ToList();

            results[methodId] = new BlastRadiusInfo(directCallers, transitiveCallers, depth, entryPointNames);
        }

        return results;
    }

    /// <summary>
    /// Computes depth (max distance from any entry point) for all methods using multi-source BFS.
    /// </summary>
    private static Dictionary<string, int> ComputeDepths(
        Dictionary<string, HashSet<string>> forwardGraph,
        HashSet<string> entryPoints)
    {
        var depths = new Dictionary<string, int>();
        var queue = new Queue<(string MethodId, int Depth)>();

        foreach (var entry in entryPoints)
        {
            depths[entry] = 0;
            queue.Enqueue((entry, 0));
        }

        while (queue.Count > 0)
        {
            var (current, currentDepth) = queue.Dequeue();

            if (!forwardGraph.TryGetValue(current, out var callees))
                continue;

            foreach (var callee in callees)
            {
                var newDepth = currentDepth + 1;
                // Track maximum depth (longest path)
                if (!depths.TryGetValue(callee, out var existingDepth) || newDepth > existingDepth)
                {
                    depths[callee] = newDepth;
                    queue.Enqueue((callee, newDepth));
                }
            }
        }

        return depths;
    }

    /// <summary>
    /// Computes transitive callers and identifies entry points that can reach this method.
    /// Uses BFS on reverse graph.
    /// </summary>
    private static (int TransitiveCallers, List<string> EntryPoints) ComputeTransitiveCallersAndEntryPoints(
        string methodId,
        Dictionary<string, HashSet<string>> reverseGraph,
        HashSet<string> allEntryPoints,
        int maxEntryPoints)
    {
        var visited = new HashSet<string> { methodId };
        var queue = new Queue<string>();
        var reachableEntryPoints = new List<string>();

        // Start from direct callers
        if (reverseGraph.TryGetValue(methodId, out var directCallers))
        {
            foreach (var caller in directCallers)
            {
                if (visited.Add(caller))
                {
                    queue.Enqueue(caller);
                    if (allEntryPoints.Contains(caller) && reachableEntryPoints.Count < maxEntryPoints)
                        reachableEntryPoints.Add(caller);
                }
            }
        }

        // BFS through transitive callers
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!reverseGraph.TryGetValue(current, out var callers))
                continue;

            foreach (var caller in callers)
            {
                if (visited.Add(caller))
                {
                    queue.Enqueue(caller);
                    if (allEntryPoints.Contains(caller) && reachableEntryPoints.Count < maxEntryPoints)
                        reachableEntryPoints.Add(caller);
                }
            }
        }

        // Transitive callers = all visited minus the method itself
        return (visited.Count - 1, reachableEntryPoints);
    }
}
