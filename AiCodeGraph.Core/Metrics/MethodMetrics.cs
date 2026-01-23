namespace AiCodeGraph.Core.Metrics;

public record MethodMetrics(string MethodId, int CognitiveComplexity, int LinesOfCode, int MaxNestingDepth);
