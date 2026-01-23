namespace AiCodeGraph.Core.Models.CodeGraph;

public record NamespaceModel(
    string Id,
    string FullName,
    List<TypeModel> Types,
    List<NamespaceModel> ChildNamespaces
);
