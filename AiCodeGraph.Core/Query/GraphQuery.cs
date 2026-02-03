namespace AiCodeGraph.Core.Query;

/// <summary>
/// Direction for graph traversal expansion.
/// </summary>
public enum ExpandDirection
{
    /// <summary>No expansion - return only seed methods.</summary>
    None,
    /// <summary>Expand to methods that call the seed methods.</summary>
    Callers,
    /// <summary>Expand to methods that the seed methods call.</summary>
    Callees,
    /// <summary>Expand in both directions.</summary>
    Both
}

/// <summary>
/// Strategy for ranking query results.
/// </summary>
public enum RankStrategy
{
    /// <summary>Order by blast radius (transitive caller count).</summary>
    BlastRadius,
    /// <summary>Order by cognitive complexity.</summary>
    Complexity,
    /// <summary>Order by coupling (afferent + efferent).</summary>
    Coupling,
    /// <summary>Order by combined risk score (complexity * log(blast_radius + 1)).</summary>
    Combined
}

/// <summary>
/// Output format for query results.
/// </summary>
public enum QueryOutputFormat
{
    /// <summary>Compact one-line-per-item format for LLM consumption.</summary>
    Compact,
    /// <summary>JSON format with full structured data.</summary>
    Json,
    /// <summary>Tabular format with aligned columns.</summary>
    Table
}

/// <summary>
/// Defines the starting point(s) for a graph query.
/// At least one property must be non-null for a valid seed.
/// </summary>
public record QuerySeed
{
    /// <summary>Exact method ID for precise lookup.</summary>
    public string? MethodId { get; init; }

    /// <summary>Fuzzy match pattern supporting wildcards (e.g., "*Repository.Get*").</summary>
    public string? MethodPattern { get; init; }

    /// <summary>Select all methods in the specified namespace.</summary>
    public string? Namespace { get; init; }

    /// <summary>Select all methods in the specified intent cluster.</summary>
    public string? Cluster { get; init; }

    /// <summary>
    /// Returns true if the seed has at least one non-null property.
    /// </summary>
    public bool IsValid =>
        MethodId != null || MethodPattern != null || Namespace != null || Cluster != null;
}

/// <summary>
/// Controls how the graph is traversed from the seed methods.
/// </summary>
public record QueryExpand
{
    /// <summary>Direction to expand (Callers, Callees, Both, or None).</summary>
    public ExpandDirection Direction { get; init; } = ExpandDirection.Both;

    /// <summary>Maximum traversal depth from seed methods.</summary>
    public int MaxDepth { get; init; } = 3;

    /// <summary>Whether to include transitive relationships (indirect connections).</summary>
    public bool IncludeTransitive { get; init; } = true;
}

/// <summary>
/// Defines inclusion and exclusion rules for query results.
/// </summary>
public record QueryFilter
{
    /// <summary>Only include methods in these namespaces (whitelist).</summary>
    public List<string>? IncludeNamespaces { get; init; }

    /// <summary>Exclude methods in these namespaces (blacklist).</summary>
    public List<string>? ExcludeNamespaces { get; init; }

    /// <summary>Only include methods in these types (whitelist).</summary>
    public List<string>? IncludeTypes { get; init; }

    /// <summary>Minimum cognitive complexity to include.</summary>
    public int? MinComplexity { get; init; }

    /// <summary>Maximum cognitive complexity to include.</summary>
    public int? MaxComplexity { get; init; }

    /// <summary>Whether to exclude test methods and test classes.</summary>
    public bool ExcludeTests { get; init; } = true;
}

/// <summary>
/// Controls how query results are ordered.
/// </summary>
public record QueryRank
{
    /// <summary>Ranking strategy to use.</summary>
    public RankStrategy Strategy { get; init; } = RankStrategy.BlastRadius;

    /// <summary>Whether to sort in descending order (highest first).</summary>
    public bool Descending { get; init; } = true;
}

/// <summary>
/// Controls output format and limits.
/// </summary>
public record QueryOutput
{
    /// <summary>Maximum number of results to return.</summary>
    public int MaxResults { get; init; } = 20;

    /// <summary>Output format (Compact, Json, or Table).</summary>
    public QueryOutputFormat Format { get; init; } = QueryOutputFormat.Compact;

    /// <summary>Whether to include complexity metrics in output.</summary>
    public bool IncludeMetrics { get; init; } = true;

    /// <summary>Whether to include file location in output.</summary>
    public bool IncludeLocation { get; init; } = true;
}

/// <summary>
/// Unified query schema for all graph operations.
/// Combines seed selection, traversal, filtering, ranking, and output configuration.
/// </summary>
public record GraphQuery
{
    /// <summary>Starting point(s) for the query. Required.</summary>
    public required QuerySeed Seed { get; init; }

    /// <summary>How to traverse the graph from the seed. Null means no expansion.</summary>
    public QueryExpand? Expand { get; init; }

    /// <summary>Filtering rules for results. Null means no filtering.</summary>
    public QueryFilter? Filter { get; init; }

    /// <summary>Ranking configuration for results. Null uses default ranking.</summary>
    public QueryRank? Rank { get; init; }

    /// <summary>Output format and limits. Null uses default output settings.</summary>
    public QueryOutput? Output { get; init; }
}
