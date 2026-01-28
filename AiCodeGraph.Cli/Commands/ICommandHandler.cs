using System.CommandLine;

namespace AiCodeGraph.Cli.Commands;

/// <summary>
/// Interface for CLI command handlers.
/// Each implementation encapsulates a single CLI command with its options and action.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Builds and returns the System.CommandLine Command with all options and the action handler.
    /// </summary>
    Command BuildCommand();
}
