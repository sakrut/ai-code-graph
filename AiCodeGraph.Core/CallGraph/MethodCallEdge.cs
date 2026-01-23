namespace AiCodeGraph.Core.CallGraph;

public record MethodCallEdge(string CallerId, string CalleeId, CallKind Kind);
