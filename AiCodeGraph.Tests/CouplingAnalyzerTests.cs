using AiCodeGraph.Core.Analysis;

namespace AiCodeGraph.Tests;

public class CouplingAnalyzerTests
{
    [Theory]
    [InlineData("void AiCodeGraph.Core.Analysis.CouplingAnalyzer.AnalyzeAsync()", "namespace", "AiCodeGraph.Core.Analysis")]
    [InlineData("void AiCodeGraph.Core.Analysis.CouplingAnalyzer.AnalyzeAsync()", "type", "AiCodeGraph.Core.Analysis.CouplingAnalyzer")]
    [InlineData("Task<List<CouplingMetrics>> AiCodeGraph.Core.Analysis.CouplingAnalyzer.AnalyzeAsync(IStorageService storage, string level)", "namespace", "AiCodeGraph.Core.Analysis")]
    [InlineData("int MyApp.Svc.GetCount()", "namespace", "MyApp")]
    [InlineData("int MyApp.Svc.GetCount()", "type", "MyApp.Svc")]
    [InlineData("System.Threading.Tasks.Task<List<int>> AiCodeGraph.Core.Storage.StorageService.GetAllAsync()", "namespace", "AiCodeGraph.Core.Storage")]
    [InlineData("System.Threading.Tasks.Task<List<int>> AiCodeGraph.Core.Storage.StorageService.GetAllAsync()", "type", "AiCodeGraph.Core.Storage.StorageService")]
    public void GetGroup_StripsReturnTypePrefix(string fullName, string level, string expected)
    {
        var result = CouplingAnalyzer.GetGroup(fullName, level);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("AiCodeGraph.Core.Analysis.CouplingAnalyzer.AnalyzeAsync()", "namespace", "AiCodeGraph.Core.Analysis")]
    [InlineData("AiCodeGraph.Core.Analysis.CouplingAnalyzer.AnalyzeAsync()", "type", "AiCodeGraph.Core.Analysis.CouplingAnalyzer")]
    public void GetGroup_WorksWithoutReturnTypePrefix(string fullName, string level, string expected)
    {
        var result = CouplingAnalyzer.GetGroup(fullName, level);
        Assert.Equal(expected, result);
    }
}
