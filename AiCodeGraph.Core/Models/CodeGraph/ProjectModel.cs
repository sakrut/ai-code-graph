namespace AiCodeGraph.Core.Models.CodeGraph;

public record ProjectModel(
    string Id,
    string Name,
    string FilePath,
    List<NamespaceModel> Namespaces
);
