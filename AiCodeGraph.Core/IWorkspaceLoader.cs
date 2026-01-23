using Microsoft.CodeAnalysis;

namespace AiCodeGraph.Core;

public interface IWorkspaceLoader
{
    Task<Solution> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);
}
