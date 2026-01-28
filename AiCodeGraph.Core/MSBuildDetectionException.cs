using System.Text;

namespace AiCodeGraph.Core;

/// <summary>
/// Exception thrown when MSBuild cannot be detected during workspace loading.
/// Provides detailed information about searched locations and actionable guidance.
/// </summary>
public class MSBuildDetectionException : InvalidOperationException
{
    public IReadOnlyList<(string Location, bool Found)> SearchedLocations { get; }

    public MSBuildDetectionException(IReadOnlyList<(string Location, bool Found)> searchedLocations)
        : base(FormatMessage(searchedLocations))
    {
        SearchedLocations = searchedLocations;
    }

    private static string FormatMessage(IReadOnlyList<(string Location, bool Found)> locations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MSBuild could not be detected.");
        sb.AppendLine();
        sb.AppendLine("Searched locations:");
        foreach (var (loc, found) in locations)
        {
            var mark = found ? "+" : "x";
            sb.AppendLine($"  {mark} {loc}");
        }
        sb.AppendLine();
        sb.AppendLine("Solutions:");
        sb.AppendLine("  1. Install Visual Studio 2022 with \".NET desktop development\" workload");
        sb.AppendLine("  2. Install VS Build Tools: https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022");
        sb.AppendLine("  3. Set MSBUILD_EXE_PATH environment variable to your MSBuild.exe location");
        sb.AppendLine();
        sb.AppendLine("Example:");
        if (OperatingSystem.IsWindows())
        {
            sb.AppendLine("  set MSBUILD_EXE_PATH=\"C:\\Path\\To\\MSBuild.exe\"");
        }
        else
        {
            sb.AppendLine("  export MSBUILD_EXE_PATH=\"/path/to/msbuild\"");
        }
        sb.AppendLine("  ai-code-graph analyze --solution MyApp.sln");

        return sb.ToString();
    }
}
