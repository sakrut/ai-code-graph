using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiCodeGraph.Core.Query;

/// <summary>
/// Serializes and deserializes GraphQuery objects to/from JSON.
/// </summary>
public static class GraphQuerySerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    /// <summary>
    /// Serializes a GraphQuery to JSON.
    /// </summary>
    public static string Serialize(GraphQuery query)
    {
        return JsonSerializer.Serialize(query, Options);
    }

    /// <summary>
    /// Deserializes a GraphQuery from JSON.
    /// </summary>
    /// <returns>The deserialized query, or null if the JSON is invalid.</returns>
    public static GraphQuery? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<GraphQuery>(json, ReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to deserialize a GraphQuery from JSON.
    /// </summary>
    public static bool TryDeserialize(string json, out GraphQuery? query, out string? error)
    {
        try
        {
            query = JsonSerializer.Deserialize<GraphQuery>(json, ReadOptions);
            if (query == null)
            {
                error = "Deserialization returned null";
                return false;
            }
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            query = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Generates a JSON Schema for GraphQuery (draft-07).
    /// </summary>
    public static string GenerateJsonSchema()
    {
        var schema = new
        {
            schema = "http://json-schema.org/draft-07/schema#",
            title = "GraphQuery",
            description = "Unified query schema for AI Code Graph operations",
            type = "object",
            required = new[] { "seed" },
            properties = new
            {
                seed = new
                {
                    type = "object",
                    description = "Starting point(s) for the query. At least one property must be non-null.",
                    properties = new
                    {
                        methodId = new { type = "string", description = "Exact method ID for precise lookup" },
                        methodPattern = new { type = "string", description = "Fuzzy match pattern supporting wildcards (e.g., *Repository.Get*)" },
                        @namespace = new { type = "string", description = "Select all methods in the specified namespace" },
                        cluster = new { type = "string", description = "Select all methods in the specified intent cluster" }
                    }
                },
                expand = new
                {
                    type = "object",
                    description = "Controls how the graph is traversed from the seed methods",
                    properties = new
                    {
                        direction = new
                        {
                            type = "string",
                            @enum = new[] { "none", "callers", "callees", "both" },
                            @default = "both",
                            description = "Direction to expand (callers, callees, both, or none)"
                        },
                        maxDepth = new
                        {
                            type = "integer",
                            minimum = 0,
                            maximum = 100,
                            @default = 3,
                            description = "Maximum traversal depth from seed methods"
                        },
                        includeTransitive = new
                        {
                            type = "boolean",
                            @default = true,
                            description = "Whether to include transitive relationships"
                        }
                    }
                },
                filter = new
                {
                    type = "object",
                    description = "Filtering rules for query results",
                    properties = new
                    {
                        includeNamespaces = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Only include methods in these namespaces (whitelist)"
                        },
                        excludeNamespaces = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Exclude methods in these namespaces (blacklist)"
                        },
                        includeTypes = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Only include methods in these types (whitelist)"
                        },
                        minComplexity = new
                        {
                            type = "integer",
                            minimum = 0,
                            description = "Minimum cognitive complexity to include"
                        },
                        maxComplexity = new
                        {
                            type = "integer",
                            minimum = 0,
                            description = "Maximum cognitive complexity to include"
                        },
                        excludeTests = new
                        {
                            type = "boolean",
                            @default = true,
                            description = "Whether to exclude test methods and test classes"
                        }
                    }
                },
                rank = new
                {
                    type = "object",
                    description = "Controls how query results are ordered",
                    properties = new
                    {
                        strategy = new
                        {
                            type = "string",
                            @enum = new[] { "blastRadius", "complexity", "coupling", "combined" },
                            @default = "blastRadius",
                            description = "Ranking strategy to use"
                        },
                        descending = new
                        {
                            type = "boolean",
                            @default = true,
                            description = "Whether to sort in descending order (highest first)"
                        }
                    }
                },
                output = new
                {
                    type = "object",
                    description = "Controls output format and limits",
                    properties = new
                    {
                        maxResults = new
                        {
                            type = "integer",
                            minimum = 1,
                            maximum = 1000,
                            @default = 20,
                            description = "Maximum number of results to return"
                        },
                        format = new
                        {
                            type = "string",
                            @enum = new[] { "compact", "json", "table" },
                            @default = "compact",
                            description = "Output format"
                        },
                        includeMetrics = new
                        {
                            type = "boolean",
                            @default = true,
                            description = "Whether to include complexity metrics in output"
                        },
                        includeLocation = new
                        {
                            type = "boolean",
                            @default = true,
                            description = "Whether to include file location in output"
                        }
                    }
                }
            },
            examples = new object[]
            {
                new
                {
                    seed = new { methodId = "MyApp.Service.GetUser(int)" }
                },
                new
                {
                    seed = new { methodPattern = "*Repository*" },
                    expand = new { direction = "callees", maxDepth = 2 },
                    filter = new { excludeNamespaces = new[] { "Tests" } },
                    output = new { maxResults = 50 }
                }
            }
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}
