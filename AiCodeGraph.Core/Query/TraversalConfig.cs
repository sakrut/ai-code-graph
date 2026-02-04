namespace AiCodeGraph.Core.Query;

public record TraversalConfig(
    string SeedMethodId,
    TraversalDirection Direction = TraversalDirection.Both,
    int MaxDepth = 3,
    TraversalStrategy Strategy = TraversalStrategy.BFS,
    RankingStrategy Ranking = RankingStrategy.BlastRadius,
    int? MaxResults = null,
    FilterConfig? Filter = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SeedMethodId))
            throw new ArgumentException("SeedMethodId is required", nameof(SeedMethodId));

        if (MaxDepth < 1)
            throw new ArgumentException("MaxDepth must be at least 1", nameof(MaxDepth));

        if (MaxResults is < 1)
            throw new ArgumentException("MaxResults must be at least 1 when specified", nameof(MaxResults));
    }
}

public record CombinedRankingWeights(
    float BlastRadiusWeight = 0.4f,
    float ComplexityWeight = 0.35f,
    float CouplingWeight = 0.25f)
{
    public void Validate()
    {
        var sum = BlastRadiusWeight + ComplexityWeight + CouplingWeight;
        if (Math.Abs(sum - 1.0f) > 0.01f)
            throw new ArgumentException($"Ranking weights should sum to 1.0 (got {sum:F2})");
    }
}
