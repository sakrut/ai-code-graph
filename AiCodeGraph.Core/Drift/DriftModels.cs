using AiCodeGraph.Core.Duplicates;

namespace AiCodeGraph.Core.Drift;

public record DriftReport(
    List<MethodDiff> NewMethods,
    List<MethodDiff> RemovedMethods,
    List<ComplexityRegression> Regressions,
    List<ClonePair> NewDuplicates,
    List<ScatteringAlert> IntentScattering);

public record MethodDiff(
    string MethodId,
    string FullName,
    string? Namespace,
    string? FilePath);

public record ComplexityRegression(
    string MethodId,
    string FullName,
    int BaselineComplexity,
    int CurrentComplexity,
    double PercentageIncrease,
    bool CrossedAbsoluteThreshold);

public record ScatteringAlert(
    string ClusterLabel,
    List<string> BaselineNamespaces,
    List<string> NewNamespaces,
    List<string> NewMemberMethods,
    int TotalMemberCount);
