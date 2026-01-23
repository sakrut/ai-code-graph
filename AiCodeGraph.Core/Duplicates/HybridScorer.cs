namespace AiCodeGraph.Core.Duplicates;

public class HybridScorer
{
    private readonly float _alpha;
    private readonly float _threshold;

    public HybridScorer(float alpha = 0.4f, float threshold = 0.5f)
    {
        _alpha = alpha;
        _threshold = threshold;
    }

    public List<ClonePair> Merge(List<ClonePair> structuralClones, List<ClonePair> semanticClones)
    {
        var merged = new Dictionary<(string, string), (float Structural, float Semantic)>();

        foreach (var pair in structuralClones)
        {
            var key = NormalizeKey(pair.MethodIdA, pair.MethodIdB);
            merged[key] = (pair.StructuralSimilarity, 0f);
        }

        foreach (var pair in semanticClones)
        {
            var key = NormalizeKey(pair.MethodIdA, pair.MethodIdB);
            if (merged.TryGetValue(key, out var existing))
                merged[key] = (existing.Structural, pair.SemanticSimilarity);
            else
                merged[key] = (0f, pair.SemanticSimilarity);
        }

        var results = new List<ClonePair>();
        foreach (var (key, scores) in merged)
        {
            var hybridScore = _alpha * scores.Structural + (1 - _alpha) * scores.Semantic;
            if (hybridScore < _threshold) continue;

            var type = ClassifyCloneType(scores.Structural, scores.Semantic);
            results.Add(new ClonePair(
                key.Item1,
                key.Item2,
                scores.Structural,
                scores.Semantic,
                hybridScore,
                type));
        }

        return results.OrderByDescending(p => p.HybridScore).ToList();
    }

    private static CloneType ClassifyCloneType(float structural, float semantic)
    {
        if (structural > 0.95f) return CloneType.Type1;
        if (structural > 0.8f) return CloneType.Type2;
        return CloneType.Semantic;
    }

    private static (string, string) NormalizeKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
    }
}
