using Microsoft.CodeAnalysis;

namespace AiCodeGraph.Core.Models;

public record WorkspaceDiagnosticInfo(
    WorkspaceDiagnosticKind Kind,
    string Message,
    string? ProjectName
);
