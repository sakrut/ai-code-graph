namespace AiCodeGraph.Core.Duplicates;

public enum CloneType
{
    Type1,    // Structural > 0.95 (exact/near-exact clones)
    Type2,    // Structural > 0.8 (renamed variables)
    Semantic  // Semantic > 0.85, structural < 0.8
}

public record ClonePair(
    string MethodIdA,
    string MethodIdB,
    float StructuralSimilarity,
    float SemanticSimilarity,
    float HybridScore,
    CloneType Type);
