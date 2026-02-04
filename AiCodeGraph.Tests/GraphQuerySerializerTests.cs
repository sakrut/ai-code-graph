using System.Text.Json;
using AiCodeGraph.Core.Query;

namespace AiCodeGraph.Tests;

public class GraphQuerySerializerTests
{
    [Fact]
    public void Serialize_Deserialize_RoundTrip_PreservesValues()
    {
        var original = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "Test.Method()" },
            Expand = new QueryExpand { Direction = ExpandDirection.Callees, MaxDepth = 5 },
            Filter = new QueryFilter
            {
                ExcludeNamespaces = new List<string> { "Tests" },
                MinComplexity = 5
            },
            Rank = new QueryRank { Strategy = RankStrategy.Complexity, Descending = false },
            Output = new QueryOutput { MaxResults = 50, Format = QueryOutputFormat.Json }
        };

        var json = GraphQuerySerializer.Serialize(original);
        var deserialized = GraphQuerySerializer.Deserialize(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Seed.MethodId, deserialized.Seed.MethodId);
        Assert.Equal(original.Expand!.Direction, deserialized.Expand!.Direction);
        Assert.Equal(original.Expand.MaxDepth, deserialized.Expand.MaxDepth);
        Assert.Equal(original.Filter!.ExcludeNamespaces, deserialized.Filter!.ExcludeNamespaces);
        Assert.Equal(original.Filter.MinComplexity, deserialized.Filter.MinComplexity);
        Assert.Equal(original.Rank!.Strategy, deserialized.Rank!.Strategy);
        Assert.Equal(original.Rank.Descending, deserialized.Rank.Descending);
        Assert.Equal(original.Output!.MaxResults, deserialized.Output!.MaxResults);
        Assert.Equal(original.Output.Format, deserialized.Output.Format);
    }

    [Fact]
    public void Serialize_EnumValues_AreStrings()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "Test" },
            Expand = new QueryExpand { Direction = ExpandDirection.Callees },
            Rank = new QueryRank { Strategy = RankStrategy.BlastRadius }
        };

        var json = GraphQuerySerializer.Serialize(query);

        Assert.Contains("\"callees\"", json.ToLower());
        Assert.Contains("\"blastRadius\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_NullOptionalProperties_AreOmitted()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "Test" }
        };

        var json = GraphQuerySerializer.Serialize(query);

        Assert.DoesNotContain("\"expand\"", json);
        Assert.DoesNotContain("\"filter\"", json);
        Assert.DoesNotContain("\"rank\"", json);
        Assert.DoesNotContain("\"output\"", json);
    }

    [Fact]
    public void Deserialize_MinimalQuery_Works()
    {
        var json = """{"seed": {"methodId": "Test.Method()"}}""";

        var query = GraphQuerySerializer.Deserialize(json);

        Assert.NotNull(query);
        Assert.Equal("Test.Method()", query.Seed.MethodId);
        Assert.Null(query.Expand);
        Assert.Null(query.Filter);
    }

    [Fact]
    public void Deserialize_MalformedJson_ReturnsNull()
    {
        var json = "{ invalid json }";

        var query = GraphQuerySerializer.Deserialize(json);

        Assert.Null(query);
    }

    [Fact]
    public void TryDeserialize_ValidJson_ReturnsTrue()
    {
        var json = """{"seed": {"methodId": "Test"}}""";

        var success = GraphQuerySerializer.TryDeserialize(json, out var query, out var error);

        Assert.True(success);
        Assert.NotNull(query);
        Assert.Null(error);
    }

    [Fact]
    public void TryDeserialize_InvalidJson_ReturnsFalseWithError()
    {
        var json = "{ invalid }";

        var success = GraphQuerySerializer.TryDeserialize(json, out var query, out var error);

        Assert.False(success);
        Assert.Null(query);
        Assert.NotNull(error);
    }

    [Fact]
    public void GenerateJsonSchema_ReturnsValidJson()
    {
        var schema = GraphQuerySerializer.GenerateJsonSchema();

        // Should be valid JSON
        var doc = JsonDocument.Parse(schema);
        Assert.NotNull(doc);

        // Should contain expected elements
        Assert.Contains("\"type\":", schema);
        Assert.Contains("\"seed\"", schema);
        Assert.Contains("\"properties\":", schema);
    }

    [Fact]
    public void GenerateJsonSchema_IncludesAllSections()
    {
        var schema = GraphQuerySerializer.GenerateJsonSchema();

        Assert.Contains("seed", schema);
        Assert.Contains("expand", schema);
        Assert.Contains("filter", schema);
        Assert.Contains("rank", schema);
        Assert.Contains("output", schema);
        Assert.Contains("examples", schema);
    }

    [Fact]
    public void Deserialize_CaseInsensitive_Works()
    {
        var json = """{"Seed": {"MethodId": "Test"}}""";

        var query = GraphQuerySerializer.Deserialize(json);

        Assert.NotNull(query);
        Assert.Equal("Test", query.Seed.MethodId);
    }

    [Fact]
    public void Serialize_CamelCase_Naming()
    {
        var query = new GraphQuery
        {
            Seed = new QuerySeed { MethodId = "Test", MethodPattern = "*Get*" },
            Expand = new QueryExpand { MaxDepth = 5, IncludeTransitive = true }
        };

        var json = GraphQuerySerializer.Serialize(query);

        Assert.Contains("\"methodId\"", json);
        Assert.Contains("\"methodPattern\"", json);
        Assert.Contains("\"maxDepth\"", json);
        Assert.Contains("\"includeTransitive\"", json);
    }

    [Fact]
    public void Deserialize_AllEnumValues_Supported()
    {
        var json = """
        {
            "seed": {"methodId": "Test"},
            "expand": {"direction": "both"},
            "rank": {"strategy": "combined"},
            "output": {"format": "table"}
        }
        """;

        var query = GraphQuerySerializer.Deserialize(json);

        Assert.NotNull(query);
        Assert.Equal(ExpandDirection.Both, query.Expand!.Direction);
        Assert.Equal(RankStrategy.Combined, query.Rank!.Strategy);
        Assert.Equal(QueryOutputFormat.Table, query.Output!.Format);
    }

    [Fact]
    public void Deserialize_FilterWithLists_PreservesAll()
    {
        var json = """
        {
            "seed": {"methodId": "Test"},
            "filter": {
                "includeNamespaces": ["MyApp.Core", "MyApp.Services"],
                "excludeNamespaces": ["Tests", "Mocks"],
                "includeTypes": ["Service", "Repository"]
            }
        }
        """;

        var query = GraphQuerySerializer.Deserialize(json);

        Assert.NotNull(query);
        Assert.NotNull(query.Filter);
        Assert.Equal(2, query.Filter.IncludeNamespaces!.Count);
        Assert.Equal(2, query.Filter.ExcludeNamespaces!.Count);
        Assert.Equal(2, query.Filter.IncludeTypes!.Count);
        Assert.Contains("MyApp.Core", query.Filter.IncludeNamespaces);
        Assert.Contains("Tests", query.Filter.ExcludeNamespaces);
    }
}
