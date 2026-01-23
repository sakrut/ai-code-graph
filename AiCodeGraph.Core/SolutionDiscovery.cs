namespace AiCodeGraph.Core;

public static class SolutionDiscovery
{
    public static string FindSolutionFile(string? explicitPath = null, string? startDirectory = null)
    {
        if (!string.IsNullOrEmpty(explicitPath))
        {
            if (!File.Exists(explicitPath))
                throw new FileNotFoundException($"Solution file not found: {explicitPath}", explicitPath);
            return Path.GetFullPath(explicitPath);
        }

        var searchDir = startDirectory ?? Directory.GetCurrentDirectory();
        return SearchForSolution(searchDir)
            ?? throw new FileNotFoundException(
                $"No .sln file found in {searchDir} or any parent directory.");
    }

    private static string? SearchForSolution(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            var slnFiles = dir.GetFiles("*.sln");
            if (slnFiles.Length == 1)
                return slnFiles[0].FullName;
            if (slnFiles.Length > 1)
                throw new InvalidOperationException(
                    $"Multiple .sln files found in {dir.FullName}: {string.Join(", ", slnFiles.Select(f => f.Name))}. Please specify one explicitly.");
            dir = dir.Parent;
        }
        return null;
    }
}
