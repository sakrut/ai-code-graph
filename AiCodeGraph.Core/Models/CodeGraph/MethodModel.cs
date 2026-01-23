using Microsoft.CodeAnalysis;

namespace AiCodeGraph.Core.Models.CodeGraph;

public record MethodModel(
    string Id,
    string Name,
    string FullName,
    string ReturnType,
    List<ParameterModel> Parameters,
    string? FilePath,
    int StartLine,
    int EndLine,
    Accessibility Accessibility,
    bool IsStatic,
    bool IsAsync,
    bool IsVirtual,
    bool IsOverride,
    bool IsAbstract
);
