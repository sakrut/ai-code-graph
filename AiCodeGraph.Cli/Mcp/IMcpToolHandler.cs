using System.Text.Json.Nodes;

namespace AiCodeGraph.Cli.Mcp;

public interface IMcpToolHandler
{
    IReadOnlyList<string> SupportedTools { get; }
    JsonArray GetToolDefinitions();
    Task<string> HandleAsync(string toolName, JsonNode? args, CancellationToken ct);
}
