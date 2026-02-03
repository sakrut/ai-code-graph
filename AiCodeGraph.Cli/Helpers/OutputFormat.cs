using System.CommandLine;

namespace AiCodeGraph.Cli.Helpers;

/// <summary>
/// Output format options for CLI commands.
/// </summary>
public enum OutputFormat
{
    /// <summary>
    /// Compact format: one line per item, bounded lists, no ASCII tables.
    /// Optimized for LLM token economy.
    /// </summary>
    Compact,

    /// <summary>
    /// Table format: aligned columns with headers.
    /// Human-readable ASCII tables.
    /// </summary>
    Table,

    /// <summary>
    /// JSON format: stable schema for scripting.
    /// </summary>
    Json,

    /// <summary>
    /// CSV format: comma-separated values.
    /// </summary>
    Csv
}

/// <summary>
/// Shared output options for CLI commands.
/// </summary>
public static class OutputOptions
{
    /// <summary>
    /// Creates a --format option with the specified default.
    /// </summary>
    public static Option<string> CreateFormatOption(OutputFormat defaultFormat = OutputFormat.Compact)
    {
        return new Option<string>("--format", "-f")
        {
            Description = "Output format: compact|table|json|csv",
            DefaultValueFactory = _ => defaultFormat.ToString().ToLowerInvariant()
        };
    }

    /// <summary>
    /// Creates a --top option for bounded lists.
    /// </summary>
    public static Option<int> CreateTopOption(int defaultValue = 20)
    {
        return new Option<int>("--top", "-t")
        {
            Description = "Maximum number of items to return",
            DefaultValueFactory = _ => defaultValue
        };
    }

    /// <summary>
    /// Creates a --db option for database path.
    /// </summary>
    public static Option<string> CreateDbOption()
    {
        return new Option<string>("--db")
        {
            Description = "Path to graph.db",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };
    }

    /// <summary>
    /// Creates a --id option for explicit method ID selection.
    /// </summary>
    public static Option<string?> CreateMethodIdOption()
    {
        return new Option<string?>("--id")
        {
            Description = "Explicit method ID (takes precedence over pattern)"
        };
    }

    /// <summary>
    /// Parses a format string to OutputFormat enum.
    /// </summary>
    public static OutputFormat ParseFormat(string? format)
    {
        return format?.ToLowerInvariant() switch
        {
            "compact" => OutputFormat.Compact,
            "table" => OutputFormat.Table,
            "json" => OutputFormat.Json,
            "csv" => OutputFormat.Csv,
            _ => OutputFormat.Compact
        };
    }

    /// <summary>
    /// Checks if the format is compact (the default for agent-facing commands).
    /// </summary>
    public static bool IsCompact(string? format) => ParseFormat(format) == OutputFormat.Compact;

    /// <summary>
    /// Checks if the format is JSON.
    /// </summary>
    public static bool IsJson(string? format) => ParseFormat(format) == OutputFormat.Json;

    /// <summary>
    /// Checks if the format is table.
    /// </summary>
    public static bool IsTable(string? format) => ParseFormat(format) == OutputFormat.Table;

    /// <summary>
    /// Checks if the format is CSV.
    /// </summary>
    public static bool IsCsv(string? format) => ParseFormat(format) == OutputFormat.Csv;
}
