namespace AiCodeGraph.Core.Models.CodeGraph;

public record ExtractionResult(
    ProjectModel Model,
    List<Relationship> Relationships
);
