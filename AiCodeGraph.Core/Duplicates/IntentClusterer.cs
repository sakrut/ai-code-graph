using AiCodeGraph.Core.Normalization;

namespace AiCodeGraph.Core.Duplicates;

public class IntentClusterer
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "set", "is", "has", "the", "a", "an", "to", "from", "of", "in",
        "on", "by", "for", "with", "and", "or", "not", "this", "that", "it",
        "void", "int", "string", "bool", "var", "new", "return", "null", "async", "await"
    };

    private readonly float _epsilon;
    private readonly int _minPoints;

    public IntentClusterer(float epsilon = 0.3f, int minPoints = 3)
    {
        _epsilon = epsilon;
        _minPoints = minPoints;
    }

    public List<IntentCluster> ClusterMethods(
        List<NormalizedMethod> methods,
        List<(string MethodId, float[] Vector)> embeddings)
    {
        if (embeddings.Count < _minPoints)
            return new List<IntentCluster>();

        var vectorMap = embeddings.ToDictionary(e => e.MethodId, e => e.Vector);
        var methodIds = embeddings.Select(e => e.MethodId).ToList();

        // DBSCAN
        var labels = Dbscan(methodIds, vectorMap);

        // Group by cluster label
        var clusters = new List<IntentCluster>();
        var groups = new Dictionary<int, List<string>>();

        for (int i = 0; i < methodIds.Count; i++)
        {
            var label = labels[i];
            if (label < 0) continue; // noise

            if (!groups.ContainsKey(label))
                groups[label] = new List<string>();
            groups[label].Add(methodIds[i]);
        }

        var methodMap = methods.ToDictionary(m => m.MethodId);
        int clusterId = 0;

        foreach (var (_, memberIds) in groups.OrderByDescending(g => g.Value.Count))
        {
            var cohesion = ComputeCohesion(memberIds, vectorMap);
            var label = GenerateLabel(memberIds, methodMap);
            var description = $"{label} ({memberIds.Count} methods, cohesion: {cohesion:F2})";

            clusters.Add(new IntentCluster(
                $"cluster-{clusterId++}",
                label,
                description,
                memberIds,
                cohesion));
        }

        return clusters;
    }

    private int[] Dbscan(List<string> methodIds, Dictionary<string, float[]> vectorMap)
    {
        int n = methodIds.Count;
        var labels = new int[n];
        Array.Fill(labels, -1); // -1 = unvisited/noise

        int currentCluster = 0;

        for (int i = 0; i < n; i++)
        {
            if (labels[i] != -1) continue;

            var neighbors = GetNeighbors(i, methodIds, vectorMap);
            if (neighbors.Count < _minPoints)
                continue; // noise point

            labels[i] = currentCluster;
            var queue = new Queue<int>(neighbors);

            while (queue.Count > 0)
            {
                var idx = queue.Dequeue();
                if (labels[idx] != -1 && labels[idx] != currentCluster)
                    continue; // already in another cluster

                labels[idx] = currentCluster;
                var innerNeighbors = GetNeighbors(idx, methodIds, vectorMap);
                if (innerNeighbors.Count >= _minPoints)
                {
                    foreach (var n2 in innerNeighbors)
                    {
                        if (labels[n2] == -1)
                            queue.Enqueue(n2);
                    }
                }
            }

            currentCluster++;
        }

        return labels;
    }

    private List<int> GetNeighbors(int pointIdx, List<string> methodIds, Dictionary<string, float[]> vectorMap)
    {
        var neighbors = new List<int>();
        var vector = vectorMap[methodIds[pointIdx]];

        for (int i = 0; i < methodIds.Count; i++)
        {
            if (i == pointIdx) continue;
            var other = vectorMap[methodIds[i]];
            var distance = CosineDistance(vector, other);
            if (distance <= _epsilon)
                neighbors.Add(i);
        }

        return neighbors;
    }

    private static float CosineDistance(float[] a, float[] b)
    {
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var magnitude = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        if (magnitude == 0) return 1f;
        var similarity = dot / magnitude;
        return 1f - similarity;
    }

    private static float ComputeCohesion(List<string> memberIds, Dictionary<string, float[]> vectorMap)
    {
        if (memberIds.Count < 2) return 1f;

        float totalSimilarity = 0;
        int pairCount = 0;

        for (int i = 0; i < memberIds.Count - 1; i++)
        {
            for (int j = i + 1; j < memberIds.Count; j++)
            {
                var dist = CosineDistance(vectorMap[memberIds[i]], vectorMap[memberIds[j]]);
                totalSimilarity += 1f - dist;
                pairCount++;
            }
        }

        return pairCount > 0 ? totalSimilarity / pairCount : 0f;
    }

    private static string GenerateLabel(List<string> memberIds, Dictionary<string, NormalizedMethod> methodMap)
    {
        var tokenFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in memberIds)
        {
            if (!methodMap.TryGetValue(id, out var method)) continue;

            var tokens = method.SemanticPayload
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !Stopwords.Contains(t) && t.Length > 2);

            foreach (var token in tokens)
            {
                tokenFrequency.TryGetValue(token, out var count);
                tokenFrequency[token] = count + 1;
            }
        }

        var topTokens = tokenFrequency
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => kv.Key.ToLowerInvariant())
            .ToList();

        return topTokens.Count > 0
            ? string.Join(" ", topTokens)
            : $"miscellaneous";
    }
}
