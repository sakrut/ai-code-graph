# Task ID: 60

**Title:** Add vswhere.exe-based MSBuild Detection

**Status:** done

**Dependencies:** None

**Priority:** high

**Description:** Implement vswhere.exe based MSBuild detection as the primary fallback when MSBuildLocator.QueryVisualStudioInstances fails, and add Visual Studio common path enumeration as a secondary fallback.

**Details:**

The current fix in WorkspaceLoader.cs queries MSBuildLocator and falls back to PATH/MSBUILD_EXE_PATH, but doesn't use vswhere.exe which is the most reliable way to find Visual Studio MSBuild on Windows.

Modify `AiCodeGraph.Core/WorkspaceLoader.cs` EnsureMSBuildRegistered method:

1. **Add vswhere.exe detection method**:
```csharp
private static string? TryFindMSBuildViaVsWhere()
{
    // vswhere is installed with VS Installer
    var vswhere = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe";
    if (!File.Exists(vswhere)) return null;
    
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = vswhere,
            Arguments = "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        var output = process?.StandardOutput.ReadLine();
        process?.WaitForExit(5000);
        return !string.IsNullOrWhiteSpace(output) && File.Exists(output) ? output : null;
    }
    catch { return null; }
}
```

2. **Add common VS path enumeration** as additional fallback:
```csharp
private static string? TryFindMSBuildInCommonPaths()
{
    var vsEditions = new[] { "Enterprise", "Professional", "Community", "BuildTools" };
    var vsVersions = new[] { "2022", "2019" };
    var programFiles = new[] {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
    };
    
    foreach (var pf in programFiles.Where(p => !string.IsNullOrEmpty(p)))
    foreach (var ver in vsVersions)
    foreach (var ed in vsEditions)
    {
        var path = Path.Combine(pf, "Microsoft Visual Studio", ver, ed, 
            "MSBuild", "Current", "Bin", "MSBuild.exe");
        if (File.Exists(path)) return path;
    }
    return null;
}
```

3. **Update EnsureMSBuildRegistered** to call these in order after MSBuildLocator fails:
   - First: MSBuildLocator.QueryVisualStudioInstances (existing)
   - Second: MSBUILD_EXE_PATH environment variable (existing)
   - Third: TryFindMSBuildViaVsWhere() (NEW)
   - Fourth: TryFindMSBuildInCommonPaths() (NEW)
   - Fifth: PATH search (existing)

4. **Track searched locations** for better error reporting:
   - Add a `List<(string Location, bool Found)> searchedLocations` to track what was checked
   - Pass this to the exception for improved error messages (Task 61)

**Test Strategy:**

1. Unit test TryFindMSBuildViaVsWhere returns null gracefully when vswhere.exe doesn't exist (Linux/macOS)
2. Unit test TryFindMSBuildInCommonPaths returns null when no VS installation exists
3. Integration test on Windows with VS installed: verify MSBuild is detected without MSBUILD_EXE_PATH
4. Test that existing MSBuildLocator path still takes precedence when it works
5. Test on Linux/macOS that code doesn't crash (graceful fallback to .NET SDK)
