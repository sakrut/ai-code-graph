namespace AiCodeGraph.Core.Query;

public enum TraversalDirection
{
    Callers,
    Callees,
    Both
}

public enum TraversalStrategy
{
    BFS,
    DFS
}

public enum RankingStrategy
{
    BlastRadius,
    Complexity,
    Coupling,
    Combined
}
