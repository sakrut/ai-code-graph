namespace AiCodeGraph.Core.Models.CodeGraph;

public record ParameterModel(
    string Name,
    string Type,
    bool IsOptional,
    string? DefaultValue
);
