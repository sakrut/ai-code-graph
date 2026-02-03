using AiCodeGraph.Core.Query;

namespace AiCodeGraph.Tests;

public class GraphQueryTests
{
    [Fact]
    public void QuerySeed_WithMethodId_IsValid()
    {
        var seed = new QuerySeed { MethodId = "MyApp.Service.GetUser(int)" };
        Assert.True(seed.IsValid);
    }

    [Fact]
    public void QuerySeed_WithMethodPattern_IsValid()
    {
        var seed = new QuerySeed { MethodPattern = "*Repository.Get*" };
        Assert.True(seed.IsValid);
    }

    [Fact]
    public void QuerySeed_WithNamespace_IsValid()
    {
        var seed = new QuerySeed { Namespace = "MyApp.Services" };
        Assert.True(seed.IsValid);
    }

    [Fact]
    public void QuerySeed_WithCluster_IsValid()
    {
        var seed = new QuerySeed { Cluster = "data-access" };
        Assert.True(seed.IsValid);
    }

    [Fact]
    public void QuerySeed_Empty_IsNotValid()
    {
        var seed = new QuerySeed();
        Assert.False(seed.IsValid);
    }

    [Fact]
    public void QueryExpand_HasCorrectDefaults()
    {
        var expand = new QueryExpand();
        Assert.Equal(ExpandDirection.Both, expand.Direction);
        Assert.Equal(3, expand.MaxDepth);
        Assert.True(expand.IncludeTransitive);
    }

    [Fact]
    public void QueryFilter_HasCorrectDefaults()
    {
        var filter = new QueryFilter();
        Assert.Null(filter.IncludeNamespaces);
        Assert.Null(filter.ExcludeNamespaces);
        Assert.Null(filter.IncludeTypes);
        Assert.Null(filter.MinComplexity);
        Assert.Null(filter.MaxComplexity);
        Assert.True(filter.ExcludeTests);
    }

    [Fact]
    public void QueryRank_HasCorrectDefaults()
    {
        var rank = new QueryRank();
        Assert.Equal(RankStrategy.BlastRadius, rank.Strategy);
        Assert.True(rank.Descending);
    }

    [Fact]
    public void QueryOutput_HasCorrectDefaults()
    {
        var output = new QueryOutput();
        Assert.Equal(20, output.MaxResults);
        Assert.Equal(QueryOutputFormat.Compact, output.Format);
        Assert.True(output.IncludeMetrics);
        Assert.True(output.IncludeLocation);
    }

    [Fact]
    public void GraphQuery_WithMinimalSeed_Works()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "test" }
        };

        Assert.NotNull(query.Seed);
        Assert.True(query.Seed.IsValid);
        Assert.Null(query.Expand);
        Assert.Null(query.Filter);
        Assert.Null(query.Rank);
        Assert.Null(query.Output);
    }

    [Fact]
    public void GraphQuery_WithAllProperties_Works()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodPattern = "*Service*" },
            Expand = new QueryExpand { Direction = ExpandDirection.Callees, MaxDepth = 5 },
            Filter = new QueryFilter
            {
                ExcludeNamespaces = new List<string> { "Tests" },
                MinComplexity = 5
            },
            Rank = new QueryRank { Strategy = RankStrategy.Complexity },
            Output = new QueryOutput { MaxResults = 50, Format = QueryOutputFormat.Json }
        };

        Assert.Equal("*Service*", query.Seed.MethodPattern);
        Assert.Equal(ExpandDirection.Callees, query.Expand!.Direction);
        Assert.Equal(5, query.Expand.MaxDepth);
        Assert.Contains("Tests", query.Filter!.ExcludeNamespaces!);
        Assert.Equal(5, query.Filter.MinComplexity);
        Assert.Equal(RankStrategy.Complexity, query.Rank!.Strategy);
        Assert.Equal(50, query.Output!.MaxResults);
        Assert.Equal(QueryOutputFormat.Json, query.Output.Format);
    }

    [Fact]
    public void QueryExpand_WithExpression_CreatesModifiedCopy()
    {
        var original = new QueryExpand();
        var modified = original with { MaxDepth = 10 };

        Assert.Equal(3, original.MaxDepth);
        Assert.Equal(10, modified.MaxDepth);
        Assert.Equal(original.Direction, modified.Direction);
    }

    [Theory]
    [InlineData(ExpandDirection.None)]
    [InlineData(ExpandDirection.Callers)]
    [InlineData(ExpandDirection.Callees)]
    [InlineData(ExpandDirection.Both)]
    public void ExpandDirection_AllValuesExist(ExpandDirection direction)
    {
        var expand = new QueryExpand { Direction = direction };
        Assert.Equal(direction, expand.Direction);
    }

    [Theory]
    [InlineData(RankStrategy.BlastRadius)]
    [InlineData(RankStrategy.Complexity)]
    [InlineData(RankStrategy.Coupling)]
    [InlineData(RankStrategy.Combined)]
    public void RankStrategy_AllValuesExist(RankStrategy strategy)
    {
        var rank = new QueryRank { Strategy = strategy };
        Assert.Equal(strategy, rank.Strategy);
    }

    [Theory]
    [InlineData(QueryOutputFormat.Compact)]
    [InlineData(QueryOutputFormat.Json)]
    [InlineData(QueryOutputFormat.Table)]
    public void OutputFormat_AllValuesExist(QueryOutputFormat format)
    {
        var output = new QueryOutput { Format = format };
        Assert.Equal(format, output.Format);
    }
}
