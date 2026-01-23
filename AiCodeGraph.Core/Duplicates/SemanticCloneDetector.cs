using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Normalization;

namespace AiCodeGraph.Core.Duplicates;

public class SemanticCloneDetector
{
    public List<ClonePair> DetectClones(
        List<(string MethodId, float[] Vector)> embeddings,
        float threshold = 0.85f,
        int k = 10)
    {
        var results = new List<ClonePair>();
        if (embeddings.Count < 2) return results;

        // Build a vector index from the embeddings
        var index = new VectorIndex();
        index.BuildIndex(embeddings);

        var seen = new HashSet<(string, string)>();

        foreach (var (methodId, vector) in embeddings)
        {
            var neighbors = index.Search(vector, k + 1); // +1 to exclude self
            foreach (var neighbor in neighbors)
            {
                if (neighbor.Id == methodId) continue;
                if (neighbor.Score < threshold) continue;

                var pairKey = string.CompareOrdinal(methodId, neighbor.Id) < 0
                    ? (methodId, neighbor.Id)
                    : (neighbor.Id, methodId);

                if (seen.Add(pairKey))
                {
                    results.Add(new ClonePair(
                        pairKey.Item1,
                        pairKey.Item2,
                        0f,
                        neighbor.Score,
                        0f,
                        CloneType.Semantic));
                }
            }
        }

        return results;
    }
}
