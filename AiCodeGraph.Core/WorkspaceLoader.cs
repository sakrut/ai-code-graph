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
            MSBuildLocator.RegisterDefaults();
            _msBuildRegistered = true;
        }
    }

    public async Task<LoadedWorkspace> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
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

        var solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);

        var compilations = new Dictionary<ProjectId, Compilation>();
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var compilation = await project.GetCompilationAsync(cancellationToken);
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

        return new LoadedWorkspace(solution, compilations.AsReadOnly(), diagnostics.AsReadOnly());
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _workspace = null;
    }
}
