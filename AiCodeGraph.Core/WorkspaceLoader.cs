using System.Diagnostics;
using AiCodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Build.Locator;

namespace AiCodeGraph.Core;

public class WorkspaceLoader : IWorkspaceLoader
{
    private static bool _msBuildRegistered;
    private static readonly object _registrationLock = new();
    private MSBuildWorkspace? _workspace;

    // Track searched locations for error reporting
    internal static List<(string Location, bool Found)> LastSearchedLocations { get; private set; } = new();

    private static void EnsureMSBuildRegistered()
    {
        if (_msBuildRegistered) return;
        lock (_registrationLock)
        {
            if (_msBuildRegistered) return;

            LastSearchedLocations = new List<(string Location, bool Found)>();

            // Query all discovery types
            var instances = MSBuildLocator.QueryVisualStudioInstances(
                new VisualStudioInstanceQueryOptions
                {
                    DiscoveryTypes = DiscoveryType.DeveloperConsole
                        | DiscoveryType.DotNetSdk
                        | DiscoveryType.VisualStudioSetup
                }).ToList();

            if (instances.Count == 0)
            {
                // Try default query without options as fallback
                instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
            }

            if (instances.Count > 0)
            {
                LastSearchedLocations.Add(("MSBuildLocator.QueryVisualStudioInstances", true));
                var instance = instances.OrderByDescending(i => i.Version).First();
                MSBuildLocator.RegisterInstance(instance);
                _msBuildRegistered = true;
                return;
            }

            LastSearchedLocations.Add(("MSBuildLocator.QueryVisualStudioInstances", false));

            // Fallback 1: MSBUILD_EXE_PATH environment variable
            var msbuildPath = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
            if (!string.IsNullOrEmpty(msbuildPath))
            {
                if (File.Exists(msbuildPath))
                {
                    LastSearchedLocations.Add(($"MSBUILD_EXE_PATH: {msbuildPath}", true));
                    var msbuildDir = Path.GetDirectoryName(msbuildPath)!;
                    MSBuildLocator.RegisterMSBuildPath(msbuildDir);
                    _msBuildRegistered = true;
                    return;
                }
                LastSearchedLocations.Add(($"MSBUILD_EXE_PATH: {msbuildPath}", false));
            }

            // Fallback 2: vswhere.exe (Windows only, most reliable VS detection)
            var vswhereResult = TryFindMSBuildViaVsWhere();
            if (!string.IsNullOrEmpty(vswhereResult))
            {
                LastSearchedLocations.Add(($"vswhere.exe: {vswhereResult}", true));
                var msbuildDir = Path.GetDirectoryName(vswhereResult)!;
                MSBuildLocator.RegisterMSBuildPath(msbuildDir);
                _msBuildRegistered = true;
                return;
            }

            // Fallback 3: Common VS installation paths
            var commonPathResult = TryFindMSBuildInCommonPaths();
            if (!string.IsNullOrEmpty(commonPathResult))
            {
                LastSearchedLocations.Add(($"Common VS path: {commonPathResult}", true));
                var msbuildDir = Path.GetDirectoryName(commonPathResult)!;
                MSBuildLocator.RegisterMSBuildPath(msbuildDir);
                _msBuildRegistered = true;
                return;
            }

            // Fallback 4: PATH search
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var p in paths)
            {
                var candidate = Path.Combine(p, "MSBuild.exe");
                if (File.Exists(candidate))
                {
                    LastSearchedLocations.Add(($"PATH: {candidate}", true));
                    var msbuildDir = Path.GetDirectoryName(candidate)!;
                    MSBuildLocator.RegisterMSBuildPath(msbuildDir);
                    _msBuildRegistered = true;
                    return;
                }
            }
            LastSearchedLocations.Add(("PATH search", false));

            throw new InvalidOperationException(
                "No instances of MSBuild could be detected. " +
                "Ensure Visual Studio, VS Build Tools, or the .NET SDK is installed. " +
                "If using .NET SDK only, ensure the SDK includes MSBuild (run 'dotnet --list-sdks' to verify).");
        }
    }

    private static string? TryFindMSBuildViaVsWhere()
    {
        // vswhere.exe is installed with VS Installer on Windows
        var vswherePaths = new[]
        {
            @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer", "vswhere.exe")
        };

        var vswhere = vswherePaths.FirstOrDefault(File.Exists);
        if (vswhere == null)
        {
            LastSearchedLocations.Add(("vswhere.exe (not found)", false));
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = vswhere,
                Arguments = "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadLine();
            process.WaitForExit(5000);

            if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
                return output;

            LastSearchedLocations.Add(($"vswhere.exe (no result)", false));
            return null;
        }
        catch
        {
            LastSearchedLocations.Add(("vswhere.exe (error)", false));
            return null;
        }
    }

    private static string? TryFindMSBuildInCommonPaths()
    {
        var vsEditions = new[] { "Enterprise", "Professional", "Community", "BuildTools" };
        var vsVersions = new[] { "2022", "2019" };
        var programFilesPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var pf in programFilesPaths.Where(p => !string.IsNullOrEmpty(p)))
        {
            foreach (var version in vsVersions)
            {
                foreach (var edition in vsEditions)
                {
                    var path = Path.Combine(pf, "Microsoft Visual Studio", version, edition, "MSBuild", "Current", "Bin", "MSBuild.exe");
                    if (File.Exists(path))
                        return path;
                }
            }
        }

        LastSearchedLocations.Add(("Common VS paths", false));
        return null;
    }

    public async Task<LoadedWorkspace> LoadSolutionAsync(
        string solutionPath,
        IProgress<WorkspaceLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}", solutionPath);

        EnsureMSBuildRegistered();

        var diagnostics = new List<WorkspaceDiagnosticInfo>();
        _workspace = MSBuildWorkspace.Create();

        _workspace.RegisterWorkspaceFailedHandler(args =>
        {
            diagnostics.Add(new WorkspaceDiagnosticInfo(
                args.Diagnostic.Kind,
                args.Diagnostic.Message,
                null));
        });

        progress?.Report(new WorkspaceLoadProgress("Loading", null, 0, 0));
        var solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken).ConfigureAwait(false);

        var projects = solution.Projects.ToList();
        var totalProjects = projects.Count;
        progress?.Report(new WorkspaceLoadProgress("Loaded", null, 0, totalProjects));

        var compilations = new Dictionary<ProjectId, Compilation>();
        for (var i = 0; i < projects.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = projects[i];
            progress?.Report(new WorkspaceLoadProgress("Compiling", project.Name, i + 1, totalProjects));

            try
            {
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation != null)
                    compilations[project.Id] = compilation;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new WorkspaceDiagnosticInfo(
                    WorkspaceDiagnosticKind.Failure,
                    $"Failed to compile {project.Name}: {ex.Message}",
                    project.Name));
            }
        }

        progress?.Report(new WorkspaceLoadProgress("Complete", null, totalProjects, totalProjects));
        return new LoadedWorkspace(solution, compilations.AsReadOnly(), diagnostics.AsReadOnly());
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _workspace = null;
    }
}
