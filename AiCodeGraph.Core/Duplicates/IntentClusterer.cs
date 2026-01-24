using AiCodeGraph.Core.Embeddings;
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

        // Build LSH index for fast neighbor queries
        var dimensions = vectorMap.Values.First().Length;
        var lsh = new LshSpatialIndex(dimensions, numHashFunctions: 32, numBands: 8, seed: 42);
        var vectors = methodIds.Select(id => vectorMap[id]).ToList();
        lsh.BuildIndex(vectors);

        int currentCluster = 0;

        for (int i = 0; i < n; i++)
        {
            if (labels[i] != -1) continue;

            var neighbors = GetNeighbors(i, methodIds, vectorMap, lsh);
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
                var innerNeighbors = GetNeighbors(idx, methodIds, vectorMap, lsh);
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

    private List<int> GetNeighbors(int pointIdx, List<string> methodIds, Dictionary<string, float[]> vectorMap, LshSpatialIndex lsh)
    {
        var neighbors = new List<int>();
        var vector = vectorMap[methodIds[pointIdx]];
        var candidates = lsh.GetCandidateNeighbors(pointIdx);

        foreach (var i in candidates)
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
        var verbCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nounCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in memberIds)
        {
            if (!methodMap.TryGetValue(id, out _)) continue;

            var shortName = ExtractShortName(id);
            var segments = SplitPascalCase(shortName);
            if (segments.Count == 0) continue;

            var verb = segments[0];
            if (!Stopwords.Contains(verb) && verb.Length > 1)
            {
                verbCounts.TryGetValue(verb, out var vc);
                verbCounts[verb] = vc + 1;
            }

            for (int i = 1; i < segments.Count; i++)
            {
                var noun = segments[i];
                if (!Stopwords.Contains(noun) && noun.Length > 2)
                {
                    nounCounts.TryGetValue(noun, out var nc);
                    nounCounts[noun] = nc + 1;
                }
            }
        }

        var topVerb = verbCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;
        var topNoun = nounCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;

        if (topVerb != null && topNoun != null)
            return $"{topVerb.ToLowerInvariant()}/{topNoun.ToLowerInvariant()} operations";
        if (topVerb != null)
            return $"{topVerb.ToLowerInvariant()} operations";
        if (topNoun != null)
            return $"{topNoun.ToLowerInvariant()} handlers";

        return "miscellaneous";
    }

    private static List<string> SplitPascalCase(string name)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var ch in name)
        {
            if (char.IsUpper(ch) && current.Length > 0)
            {
                segments.Add(current.ToString());
                current.Clear();
            }
            current.Append(ch);
        }

        if (current.Length > 0)
            segments.Add(current.ToString());

        return segments;
    }

    private static string ExtractShortName(string methodId)
    {
        var parenIdx = methodId.IndexOf('(');
        var nameOnly = parenIdx >= 0 ? methodId[..parenIdx] : methodId;
        var lastDot = nameOnly.LastIndexOf('.');
        return lastDot >= 0 ? nameOnly[(lastDot + 1)..] : nameOnly;
    }
}
