using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Build.Locator;

namespace AiCodeGraph.Core;

public class WorkspaceLoader : IWorkspaceLoader
{
    private static bool _msBuildRegistered;

    public async Task<Solution> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        if (!_msBuildRegistered)
        {
            MSBuildLocator.RegisterDefaults();
            _msBuildRegistered = true;
        }

        var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        return solution;
    }
}
