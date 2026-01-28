namespace AiCodeGraph.Cli.Helpers;

/// <summary>
/// Helper methods for formatting output.
/// </summary>
public static class OutputHelpers
{
    public static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    public static void PrintCallTree(
        string nodeId,
        List<(string From, string To)> edges,
        List<(string Id, string FullName, int Depth, string Direction)> nodes,
        int currentDepth,
        int maxDepth,
        HashSet<string> printed)
    {
        if (currentDepth > maxDepth) return;
        var indent = new string(' ', currentDepth * 2);

        // callees
        foreach (var edge in edges.Where(e => e.From == nodeId))
        {
            var node = nodes.FirstOrDefault(n => n.Id == edge.To);
            if (node == default) continue;
            var marker = printed.Add(edge.To) ? "" : " (*)";
            Console.WriteLine($"{indent}\u2192 {node.FullName}{marker}");
            if (marker == "")
                PrintCallTree(edge.To, edges, nodes, currentDepth + 1, maxDepth, printed);
        }

        // callers
        foreach (var edge in edges.Where(e => e.To == nodeId))
        {
            var node = nodes.FirstOrDefault(n => n.Id == edge.From);
            if (node == default) continue;
            var marker = printed.Add(edge.From) ? "" : " (*)";
            Console.WriteLine($"{indent}\u2190 {node.FullName}{marker}");
            if (marker == "")
                PrintCallTree(edge.From, edges, nodes, currentDepth + 1, maxDepth, printed);
        }
    }

    public static string FormatAge(TimeSpan age)
    {
        if (age.TotalDays < 1) return "today";
        if (age.TotalDays < 2) return "1d ago";
        if (age.TotalDays < 30) return $"{(int)age.TotalDays}d ago";
        if (age.TotalDays < 365) return $"{(int)(age.TotalDays / 30)}mo ago";
        return $"{(int)(age.TotalDays / 365)}y ago";
    }
}
