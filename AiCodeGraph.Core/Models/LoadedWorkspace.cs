using Microsoft.CodeAnalysis;

namespace AiCodeGraph.Core.Models;

public record LoadedWorkspace(
    Solution Solution,
    IReadOnlyDictionary<ProjectId, Compilation> Compilations,
    IReadOnlyList<WorkspaceDiagnosticInfo> Diagnostics
)
{
    public bool HasErrors => Diagnostics.Any(d => d.Kind == WorkspaceDiagnosticKind.Failure);
}
