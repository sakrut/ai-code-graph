using AiCodeGraph.Core.Query;

namespace AiCodeGraph.Tests;

public class GraphQueryValidatorTests
{
    private readonly GraphQueryValidator _validator = new();

    [Fact]
    public void Validate_ValidQueryWithMinimalSeed_Succeeds()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "MyApp.Service.GetUser(int)" }
        };

        var result = _validator.Validate(query);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ValidQueryWithAllOptions_Succeeds()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodPattern = "*Service*" },
            Expand = new QueryExpand { Direction = ExpandDirection.Both, MaxDepth = 5 },
            Filter = new QueryFilter
            {
                ExcludeNamespaces = new List<string> { "Tests" },
                MinComplexity = 5,
                MaxComplexity = 50
            },
            Output = new QueryOutput { MaxResults = 100 }
        };

        var result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptySeed_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed()
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one non-null property"));
    }

    [Fact]
    public void Validate_WhitespaceMethodId_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "   " }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MethodId cannot be empty"));
    }

    [Fact]
    public void Validate_TooLongPattern_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodPattern = new string('x', 501) }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("exceeds maximum length"));
    }

    [Fact]
    public void Validate_NegativeMaxDepth_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Expand = new QueryExpand { MaxDepth = -1 }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxDepth must be >= 0"));
    }

    [Fact]
    public void Validate_MaxDepthExceedsLimit_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Expand = new QueryExpand { MaxDepth = 101 }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxDepth must be <= 100"));
    }

    [Fact]
    public void Validate_TransitiveWithDirectionNone_WarnsAboutNoEffect()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Expand = new QueryExpand { Direction = ExpandDirection.None, IncludeTransitive = true }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("IncludeTransitive has no effect"));
    }

    [Fact]
    public void Validate_NegativeMinComplexity_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Filter = new QueryFilter { MinComplexity = -1 }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MinComplexity must be >= 0"));
    }

    [Fact]
    public void Validate_MinComplexityGreaterThanMax_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Filter = new QueryFilter { MinComplexity = 50, MaxComplexity = 10 }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MinComplexity cannot be greater than"));
    }

    [Fact]
    public void Validate_OverlappingIncludeExcludeNamespaces_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Filter = new QueryFilter
            {
                IncludeNamespaces = new List<string> { "MyApp.Services", "MyApp.Data" },
                ExcludeNamespaces = new List<string> { "MyApp.Services", "MyApp.Tests" }
            }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("overlapping Include/Exclude namespaces"));
    }

    [Fact]
    public void Validate_ZeroMaxResults_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Output = new QueryOutput { MaxResults = 0 }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxResults must be >= 1"));
    }

    [Fact]
    public void Validate_MaxResultsExceedsLimit_Fails()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Output = new QueryOutput { MaxResults = 1001 }
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxResults must be <= 1000"));
    }

    [Fact]
    public void Validate_ValidMaxResultsAtBoundary_Succeeds()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" },
            Output = new QueryOutput { MaxResults = 1000 }
        };

        var result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MultipleErrors_CollectsAll()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "   " }, // Error 1
            Expand = new QueryExpand { MaxDepth = -1 }, // Error 2
            Filter = new QueryFilter { MinComplexity = 100, MaxComplexity = 10 }, // Error 3
            Output = new QueryOutput { MaxResults = 0 } // Error 4
        };

        var result = _validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 4);
    }

    [Fact]
    public void Validate_ExtensionMethod_Works()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" }
        };

        var result = query.Validate();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidationResult_Success_HasNoErrors()
    {
        var result = ValidationResult.Success();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidationResult_Failure_HasErrors()
    {
        var result = ValidationResult.Failure("Error 1", "Error 2");

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }
}
