using System.Text.Json.Nodes;
using AiCodeGraph.Core.Models.CodeGraph;

namespace AiCodeGraph.Cli.Mcp;

public static class McpProtocolHelpers
{
    public static JsonObject CreateToolDef(string name, string description, JsonObject inputSchema)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    public static JsonNode CreateResult(JsonNode? id, JsonNode result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result
        };
    }

    public static JsonNode CreateError(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    public static JsonNode CreateToolResult(JsonNode? id, string text, bool isError)
    {
        return CreateResult(id, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = isError
        });
    }

    public static string FormatAge(TimeSpan age)
    {
        if (age.TotalDays < 1) return "today";
        if (age.TotalDays < 2) return "1d ago";
        if (age.TotalDays < 30) return $"{(int)age.TotalDays}d ago";
        if (age.TotalDays < 365) return $"{(int)(age.TotalDays / 30)}mo ago";
        return $"{(int)(age.TotalDays / 365)}y ago";
    }

    public static int CountMethodsInNamespace(NamespaceModel ns)
    {
        return ns.Types.Sum(t => t.Methods.Count + t.NestedTypes.Sum(CountMethodsInType))
            + ns.ChildNamespaces.Sum(CountMethodsInNamespace);
    }

    public static int CountMethodsInType(TypeModel type)
    {
        return type.Methods.Count + type.NestedTypes.Sum(CountMethodsInType);
    }
}
