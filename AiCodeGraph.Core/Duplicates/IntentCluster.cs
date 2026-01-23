namespace AiCodeGraph.Core.Duplicates;

public record IntentCluster(
    string Id,
    string Label,
    string Description,
    List<string> MethodIds,
    float Cohesion);
