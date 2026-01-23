using AiCodeGraph.Core.Normalization;

namespace AiCodeGraph.Core.Duplicates;

public class StructuralCloneDetector
{
    public List<ClonePair> DetectClones(List<NormalizedMethod> methods, float threshold = 0.8f)
    {
        var results = new List<ClonePair>();
        if (methods.Count < 2) return results;

        for (int i = 0; i < methods.Count - 1; i++)
        {
            var tokensA = Tokenize(methods[i].StructuralSignature);
            if (tokensA.Count == 0) continue;

            for (int j = i + 1; j < methods.Count; j++)
            {
                var tokensB = Tokenize(methods[j].StructuralSignature);
                if (tokensB.Count == 0) continue;

                // Early termination: if length difference is too large, Jaccard can't exceed threshold
                int maxPossibleIntersection = Math.Min(tokensA.Count, tokensB.Count);
                int minPossibleUnion = Math.Max(tokensA.Count, tokensB.Count);
                if (minPossibleUnion > 0 && (float)maxPossibleIntersection / minPossibleUnion < threshold)
                    continue;

                var similarity = JaccardSimilarity(tokensA, tokensB);
                if (similarity >= threshold)
                {
                    var type = similarity > 0.95f ? CloneType.Type1 : CloneType.Type2;
                    results.Add(new ClonePair(
                        methods[i].MethodId,
                        methods[j].MethodId,
                        similarity,
                        0f,
                        0f,
                        type));
                }
            }
        }

        return results;
    }

    private static float JaccardSimilarity(List<string> a, List<string> b)
    {
        var setA = new HashSet<string>(a);
        var setB = new HashSet<string>(b);

        int intersection = setA.Count(t => setB.Contains(t));
        int union = setA.Count + setB.Count - intersection;

        return union == 0 ? 0f : (float)intersection / union;
    }

    private static List<string> Tokenize(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return new List<string>();

        return signature
            .Split(new[] { ' ', '(', ')', '{', '}', ';', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}
