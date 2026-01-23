using Microsoft.CodeAnalysis;

namespace AiCodeGraph.Core.Models.CodeGraph;

public record TypeModel(
    string Id,
    string Name,
    string FullName,
    TypeKind Kind,
    List<MethodModel> Methods,
    List<string> ImplementedInterfaces,
    Accessibility Accessibility,
    bool IsStatic,
    bool IsAbstract,
    bool IsSealed,
    bool IsGeneric,
    List<string> TypeParameters,
    List<TypeModel> NestedTypes
);
