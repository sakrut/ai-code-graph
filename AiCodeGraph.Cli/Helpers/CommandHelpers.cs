namespace AiCodeGraph.Cli.Helpers;

/// <summary>
/// Common helper methods for command handlers.
/// </summary>
public static class CommandHelpers
{
    /// <summary>
    /// Handles common errors in command execution.
    /// </summary>
    public static void HandleCommandError(Exception ex, bool verbose = false)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        if (verbose) Console.Error.WriteLine(ex.StackTrace);
        Environment.ExitCode = 1;
    }

    /// <summary>
    /// Checks if the database file exists and reports an error if not.
    /// </summary>
    /// <returns>True if database exists, false otherwise.</returns>
    public static bool ValidateDatabase(string dbPath)
    {
        if (File.Exists(dbPath)) return true;

        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return false;
    }
}
