using AiCodeGraph.Core.Models;

namespace AiCodeGraph.Core;

public interface IWorkspaceLoader : IDisposable
{
    Task<LoadedWorkspace> LoadSolutionAsync(
        string solutionPath,
        IProgress<WorkspaceLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
