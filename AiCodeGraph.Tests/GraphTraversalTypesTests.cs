using AiCodeGraph.Core.Query;

namespace AiCodeGraph.Tests;

public class GraphTraversalTypesTests
{
    [Fact]
    public void TraversalConfig_Validate_ThrowsOnEmptySeedMethodId()
    {
        var config = new TraversalConfig("");
        Assert.Throws<ArgumentException>(() => config.Validate());
    }

    [Fact]
    public void TraversalConfig_Validate_ThrowsOnZeroMaxDepth()
    {
        var config = new TraversalConfig("Test.Method()", MaxDepth: 0);
        Assert.Throws<ArgumentException>(() => config.Validate());
    }

    [Fact]
    public void TraversalConfig_Validate_ThrowsOnZeroMaxResults()
    {
        var config = new TraversalConfig("Test.Method()", MaxResults: 0);
        Assert.Throws<ArgumentException>(() => config.Validate());
    }

    [Fact]
    public void TraversalConfig_Validate_PassesWithValidConfig()
    {
        var config = new TraversalConfig("Test.Method()", MaxDepth: 5, MaxResults: 10);
        config.Validate(); // Should not throw
    }

    [Fact]
    public void TraversalConfig_DefaultValues_AreCorrect()
    {
        var config = new TraversalConfig("Test.Method()");
        Assert.Equal(TraversalDirection.Both, config.Direction);
        Assert.Equal(3, config.MaxDepth);
        Assert.Equal(TraversalStrategy.BFS, config.Strategy);
        Assert.Equal(RankingStrategy.BlastRadius, config.Ranking);
        Assert.Null(config.MaxResults);
        Assert.Null(config.Filter);
    }

    [Fact]
    public void CombinedRankingWeights_Validate_ThrowsOnInvalidSum()
    {
        var weights = new CombinedRankingWeights(0.5f, 0.5f, 0.5f);
        Assert.Throws<ArgumentException>(() => weights.Validate());
    }

    [Fact]
    public void CombinedRankingWeights_Validate_PassesWithValidSum()
    {
        var weights = new CombinedRankingWeights(0.4f, 0.35f, 0.25f);
        weights.Validate(); // Should not throw
    }

    [Theory]
    [InlineData("MyNamespace.MyClass.Method()", null, true)]
    [InlineData("MyNamespace.MyClass.Method()", "Public", true)]
    public void FilterConfig_Matches_NoFiltersMatchesAll(string fullName, string? accessibility, bool expected)
    {
        var filter = new FilterConfig();
        Assert.Equal(expected, filter.Matches(fullName, accessibility));
    }

    [Theory]
    [InlineData("MyNamespace.MyClass.Method()", new[] { "MyNamespace*" }, true)]
    [InlineData("MyNamespace.MyClass.Method()", new[] { "OtherNamespace*" }, false)]
    [InlineData("MyNamespace.MyClass.Method()", new[] { "*MyClass*" }, true)]
    public void FilterConfig_Matches_IncludeNamespacesFilters(string fullName, string[] patterns, bool expected)
    {
        var filter = new FilterConfig(IncludeNamespaces: patterns);
        Assert.Equal(expected, filter.Matches(fullName, null));
    }

    [Theory]
    [InlineData("MyNamespace.MyClass.Method()", new[] { "MyNamespace*" }, false)]
    [InlineData("MyNamespace.MyClass.Method()", new[] { "OtherNamespace*" }, true)]
    public void FilterConfig_Matches_ExcludeNamespacesFilters(string fullName, string[] patterns, bool expected)
    {
        var filter = new FilterConfig(ExcludeNamespaces: patterns);
        Assert.Equal(expected, filter.Matches(fullName, null));
    }

    [Fact]
    public void FilterConfig_Matches_AccessibilityFilters()
    {
        var filter = new FilterConfig(IncludeAccessibility: new[] { "Public", "Internal" });
        Assert.True(filter.Matches("Test.Method()", "Public"));
        Assert.True(filter.Matches("Test.Method()", "Internal"));
        Assert.False(filter.Matches("Test.Method()", "Private"));
    }

    [Fact]
    public void FilterConfig_Matches_ExcludesGeneratedCode()
    {
        var filter = new FilterConfig(ExcludeGeneratedCode: true);
        Assert.False(filter.Matches("Test.g.cs.Method()", null));
        Assert.False(filter.Matches("Test.<GeneratedMethod>d__1.MoveNext()", null));
        Assert.True(filter.Matches("Test.Method()", null));
    }

    [Fact]
    public void TraversalResult_Empty_CreatesEmptyResult()
    {
        var config = new TraversalConfig("Test.Method()");
        var result = TraversalResult.Empty("Test.Method()", config);

        Assert.Equal("Test.Method()", result.SeedMethodId);
        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
        Assert.Equal(0, result.TotalNodesVisited);
    }

    [Fact]
    public void TraversalNode_RankingScoreIsSettable()
    {
        var node = new TraversalNode("id", "FullName", 1, TraversalDirection.Callers);
        Assert.Equal(0, node.RankingScore);

        node.RankingScore = 5.5f;
        Assert.Equal(5.5f, node.RankingScore);
    }
}
