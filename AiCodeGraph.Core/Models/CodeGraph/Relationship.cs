namespace AiCodeGraph.Core.Models.CodeGraph;

public record Relationship(string SourceId, string TargetId, RelationshipKind Kind);

public enum RelationshipKind
{
    Contains,
    Implements,
    Overrides,
    Calls
}
