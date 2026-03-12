using System.CommandLine;

namespace AiCodeGraph.Cli.Commands;

/// <summary>
/// Central registry that builds the root command with all CLI subcommands.
/// </summary>
public static class CommandRegistry
{
    public static RootCommand Build()
    {
        var rootCommand = new RootCommand("AI Code Graph - Semantic code analysis for .NET");

        var handlers = new ICommandHandler[]
        {
            new AnalyzeCommand(),
            new CallgraphCommand(),
            new HotspotsCommand(),
            new TreeCommand(),
            new SimilarCommand(),
            new DuplicatesCommand(),
            new ClustersCommand(),
            new TokenSearchCommand(),
            new SemanticSearchCommand(),
            new ExportCommand(),
            new DriftCommand(),
            new ContextCommand(),
            new ImpactCommand(),
            new DeadCodeCommand(),
            new ChurnCommand(),
            new CouplingCommand(),
            new DiffCommand(),
            new McpCommand(),
            new SetupClaudeCommand(),
            new SetupCursorCommand(),
            new SetupCodexCommand(),
            new StatusCommand(),
            new LayersCommand(),
            new QueryCommand(),
            new CheckDepsCommand()
        };

        foreach (var handler in handlers)
        {
            rootCommand.Add(handler.BuildCommand());
        }

        return rootCommand;
    }
}
