namespace AiCodeGraph.Core.Query;

/// <summary>
/// Result of validating a GraphQuery.
/// </summary>
public record ValidationResult
{
    /// <summary>Whether the query passed validation.</summary>
    public bool IsValid { get; init; }

    /// <summary>List of validation error messages.</summary>
    public List<string> Errors { get; init; } = new();

    /// <summary>Creates a successful validation result.</summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>Creates a failed validation result with error messages.</summary>
    public static ValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = errors.ToList() };

    /// <summary>Creates a failed validation result from a list of errors.</summary>
    public static ValidationResult Failure(List<string> errors) =>
        new() { IsValid = false, Errors = errors };
}

/// <summary>
/// Validates GraphQuery objects before execution.
/// </summary>
public class GraphQueryValidator
{
    private const int MaxPatternLength = 500;
    private const int MaxDepthLimit = 100;
    private const int MaxResultsLimit = 1000;

    /// <summary>
    /// Validates a GraphQuery and returns any validation errors.
    /// </summary>
    public ValidationResult Validate(GraphQuery query)
    {
        var errors = new List<string>();

        ValidateSeed(query.Seed, errors);

        if (query.Expand != null)
            ValidateExpand(query.Expand, errors);

        if (query.Filter != null)
            ValidateFilter(query.Filter, errors);

        if (query.Output != null)
            ValidateOutput(query.Output, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    private static void ValidateSeed(QuerySeed seed, List<string> errors)
    {
        if (!seed.IsValid)
        {
            errors.Add("Seed must have at least one non-null property (MethodId, MethodPattern, Namespace, or Cluster)");
            return;
        }

        if (seed.MethodId != null && string.IsNullOrWhiteSpace(seed.MethodId))
            errors.Add("Seed.MethodId cannot be empty or whitespace");

        if (seed.MethodPattern != null)
        {
            if (string.IsNullOrWhiteSpace(seed.MethodPattern))
                errors.Add("Seed.MethodPattern cannot be empty or whitespace");
            else if (seed.MethodPattern.Length > MaxPatternLength)
                errors.Add($"Seed.MethodPattern exceeds maximum length of {MaxPatternLength} characters");
        }

        if (seed.Namespace != null && string.IsNullOrWhiteSpace(seed.Namespace))
            errors.Add("Seed.Namespace cannot be empty or whitespace");

        if (seed.Cluster != null && string.IsNullOrWhiteSpace(seed.Cluster))
            errors.Add("Seed.Cluster cannot be empty or whitespace");
    }

    private static void ValidateExpand(QueryExpand expand, List<string> errors)
    {
        if (expand.MaxDepth < 0)
            errors.Add("Expand.MaxDepth must be >= 0");
        else if (expand.MaxDepth > MaxDepthLimit)
            errors.Add($"Expand.MaxDepth must be <= {MaxDepthLimit} to prevent runaway traversals");

        if (expand.Direction == ExpandDirection.None && expand.IncludeTransitive)
            errors.Add("Expand.IncludeTransitive has no effect when Direction is None");
    }

    private static void ValidateFilter(QueryFilter filter, List<string> errors)
    {
        if (filter.MinComplexity.HasValue && filter.MinComplexity.Value < 0)
            errors.Add("Filter.MinComplexity must be >= 0");

        if (filter.MaxComplexity.HasValue && filter.MaxComplexity.Value < 0)
            errors.Add("Filter.MaxComplexity must be >= 0");

        if (filter.MinComplexity.HasValue && filter.MaxComplexity.HasValue &&
            filter.MinComplexity.Value > filter.MaxComplexity.Value)
        {
            errors.Add("Filter.MinComplexity cannot be greater than Filter.MaxComplexity");
        }

        // Check for overlapping include/exclude namespaces
        if (filter.IncludeNamespaces != null && filter.ExcludeNamespaces != null)
        {
            var overlapping = filter.IncludeNamespaces
                .Intersect(filter.ExcludeNamespaces, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (overlapping.Count > 0)
            {
                errors.Add($"Filter has overlapping Include/Exclude namespaces: {string.Join(", ", overlapping)}");
            }
        }

        // Validate namespace patterns
        if (filter.IncludeNamespaces != null)
        {
            foreach (var ns in filter.IncludeNamespaces)
            {
                if (string.IsNullOrWhiteSpace(ns))
                    errors.Add("Filter.IncludeNamespaces contains empty or whitespace entry");
            }
        }

        if (filter.ExcludeNamespaces != null)
        {
            foreach (var ns in filter.ExcludeNamespaces)
            {
                if (string.IsNullOrWhiteSpace(ns))
                    errors.Add("Filter.ExcludeNamespaces contains empty or whitespace entry");
            }
        }

        if (filter.IncludeTypes != null)
        {
            foreach (var type in filter.IncludeTypes)
            {
                if (string.IsNullOrWhiteSpace(type))
                    errors.Add("Filter.IncludeTypes contains empty or whitespace entry");
            }
        }
    }

    private static void ValidateOutput(QueryOutput output, List<string> errors)
    {
        if (output.MaxResults < 1)
            errors.Add("Output.MaxResults must be >= 1");
        else if (output.MaxResults > MaxResultsLimit)
            errors.Add($"Output.MaxResults must be <= {MaxResultsLimit}");
    }
}

/// <summary>
/// Extension methods for GraphQuery validation.
/// </summary>
public static class GraphQueryExtensions
{
    private static readonly GraphQueryValidator _validator = new();

    /// <summary>
    /// Validates this GraphQuery and returns the validation result.
    /// </summary>
    public static ValidationResult Validate(this GraphQuery query) =>
        _validator.Validate(query);
}
