# Task ID: 61

**Title:** Improve MSBuild Error Messages with Actionable Guidance

**Status:** done

**Dependencies:** 60 ✓

**Priority:** high

**Description:** Enhance the MSBuild detection failure error message to list all searched locations and provide clear, actionable installation guidance for users.

**Details:**

The current error message is generic and unhelpful. Update WorkspaceLoader.cs to provide detailed diagnostic information.

1. **Create MSBuildDetectionException class** in Core project:
```csharp
public class MSBuildDetectionException : InvalidOperationException
{
    public IReadOnlyList<(string Location, bool Found, string? Reason)> SearchedLocations { get; }
    
    public MSBuildDetectionException(
        IReadOnlyList<(string Location, bool Found, string? Reason)> searchedLocations)
        : base(FormatMessage(searchedLocations))
    {
        SearchedLocations = searchedLocations;
    }
    
    private static string FormatMessage(
        IReadOnlyList<(string Location, bool Found, string? Reason)> locations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MSBuild could not be detected.");
        sb.AppendLine();
        sb.AppendLine("Searched locations:");
        foreach (var (loc, found, reason) in locations)
        {
            var mark = found ? "✓" : "✗";
            var extra = reason != null ? $" ({reason})" : "";
            sb.AppendLine($"  {mark} {loc}{extra}");
        }
        sb.AppendLine();
        sb.AppendLine("Solutions:");
        sb.AppendLine("  1. Install Visual Studio 2022 with \".NET desktop development\" workload");
        sb.AppendLine("  2. Install VS Build Tools: https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022");
        sb.AppendLine("  3. Set MSBUILD_EXE_PATH environment variable to your MSBuild.exe location");
        sb.AppendLine();
        sb.AppendLine("Example:");
        if (OperatingSystem.IsWindows())
            sb.AppendLine("  set MSBUILD_EXE_PATH=\"C:\\Path\\To\\MSBuild.exe\"");
        else
            sb.AppendLine("  export MSBUILD_EXE_PATH=\"/path/to/msbuild\"");
        sb.AppendLine("  ai-code-graph analyze --solution MyApp.sln");
        return sb.ToString();
    }
}
```

2. **Update EnsureMSBuildRegistered** to track and report locations:
```csharp
var searchedLocations = new List<(string, bool, string?)>();

// After MSBuildLocator query
searchedLocations.Add(("MSBuildLocator.QueryVisualStudioInstances()", instances.Count > 0, 
    instances.Count == 0 ? "No instances found" : null));

// After MSBUILD_EXE_PATH check
var envPath = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
searchedLocations.Add(("MSBUILD_EXE_PATH environment variable", 
    !string.IsNullOrEmpty(envPath) && File.Exists(envPath),
    string.IsNullOrEmpty(envPath) ? "Not set" : !File.Exists(envPath) ? "File not found" : null));

// ... similar for vswhere, common paths, PATH search

throw new MSBuildDetectionException(searchedLocations);
```

3. **Update CLI error handling** in Program.cs to format the exception nicely:
```csharp
catch (MSBuildDetectionException ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}
```

**Test Strategy:**

1. Unit test MSBuildDetectionException formats message correctly with searched locations
2. Test that checkmarks and X marks appear correctly for found/not-found locations
3. Test Windows vs Linux/macOS example commands differ appropriately
4. Integration test: temporarily remove MSBuild access and verify error message is helpful
5. Verify exception is properly caught and displayed by CLI
