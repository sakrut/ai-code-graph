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

    private static void EnsureMSBuildRegistered()
    {
        if (_msBuildRegistered) return;
        lock (_registrationLock)
        {
            if (_msBuildRegistered) return;

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

            if (instances.Count == 0)
            {
                // Try to detect MSBuild from MSBUILD_EXE_PATH or PATH
                var msbuildPath = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
                if (string.IsNullOrEmpty(msbuildPath))
                {
                    // Try to find MSBuild in PATH
                    var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                    var paths = pathEnv.Split(Path.PathSeparator);
                    foreach (var p in paths)
                    {
                        var candidate = Path.Combine(p, "MSBuild.exe");
                        if (File.Exists(candidate))
                        {
                            msbuildPath = candidate;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(msbuildPath) && File.Exists(msbuildPath))
                {
                    var msbuildDir = Path.GetDirectoryName(msbuildPath)!;
                    MSBuildLocator.RegisterMSBuildPath(msbuildDir);
                    _msBuildRegistered = true;
                    return;
                }

                throw new InvalidOperationException(
                    "No instances of MSBuild could be detected. " +
                    "Ensure Visual Studio, VS Build Tools, or the .NET SDK is installed. " +
                    "If using .NET SDK only, ensure the SDK includes MSBuild (run 'dotnet --list-sdks' to verify).");
            }

            // Prefer the latest version
            var instance = instances.OrderByDescending(i => i.Version).First();
            MSBuildLocator.RegisterInstance(instance);
            _msBuildRegistered = true;
        }
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
