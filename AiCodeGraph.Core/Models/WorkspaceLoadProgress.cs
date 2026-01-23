namespace AiCodeGraph.Core.Models;

public record WorkspaceLoadProgress(
    string Phase,
    string? ProjectName,
    int CurrentProject,
    int TotalProjects
);
