using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Cli.Mcp;

namespace AiCodeGraph.Cli.Commands;

public class McpCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("mcp", "Run as MCP server (JSON-RPC over stdin/stdout)")
        {
            dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var server = new McpServer(dbPath);
            await server.RunAsync(cancellationToken);
        });

        return command;
    }
}
