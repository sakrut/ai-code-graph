using AiCodeGraph.Core.CallGraph;

namespace AiCodeGraph.Tests;

public class CallGraphBuilderTests
{
    private readonly CallGraphBuilder _builder = new();

    [Fact]
    public void DetectsDirectMethodCall()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public void Caller() { Helper(); }
        private void Helper() { }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Single(edges);
        Assert.Equal(CallKind.Direct, edges[0].Kind);
        Assert.Contains("Caller", edges[0].CallerId);
        Assert.Contains("Helper", edges[0].CalleeId);
    }

    [Fact]
    public void DetectsMultipleCalls()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public void Caller() { A(); B(); }
        private void A() { }
        private void B() { }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Contains("Caller", e.CallerId));
    }

    [Fact]
    public void DetectsConstructorCall()
    {
        var source = @"
namespace MyApp
{
    public class Widget
    {
        public Widget(int x) { }
    }
    public class Factory
    {
        public Widget Create() { return new Widget(1); }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Single(edges);
        Assert.Equal(CallKind.Constructor, edges[0].Kind);
        Assert.Contains("Create", edges[0].CallerId);
    }

    [Fact]
    public void DetectsVirtualMethodCall()
    {
        var source = @"
namespace MyApp
{
    public class Base
    {
        public virtual void Do() { }
    }
    public class Derived : Base
    {
        public override void Do() { }
    }
    public class Consumer
    {
        public void Run(Base b) { b.Do(); }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        var callEdge = Assert.Single(edges);
        Assert.Equal(CallKind.Virtual, callEdge.Kind);
        Assert.Contains("Run", callEdge.CallerId);
    }

    [Fact]
    public void DetectsInterfaceMethodCall()
    {
        var source = @"
namespace MyApp
{
    public interface IService { void Execute(); }
    public class Consumer
    {
        public void Run(IService svc) { svc.Execute(); }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        var callEdge = Assert.Single(edges);
        Assert.Equal(CallKind.Interface, callEdge.Kind);
        Assert.Contains("Run", callEdge.CallerId);
    }

    [Fact]
    public void DetectsConstructorInitializer()
    {
        var source = @"
namespace MyApp
{
    public class Base
    {
        public Base(int x) { }
    }
    public class Derived : Base
    {
        public Derived() : base(42) { }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        var callEdge = Assert.Single(edges);
        Assert.Equal(CallKind.Constructor, callEdge.Kind);
    }

    [Fact]
    public void DeduplicatesEdges()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public void Caller() { Helper(); Helper(); Helper(); }
        private void Helper() { }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Single(edges);
    }

    [Fact]
    public void DoesNotCreateSelfEdge()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public void Recursive() { Recursive(); }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Empty(edges);
    }

    [Fact]
    public void HandlesChainedCalls()
    {
        var source = @"
namespace MyApp
{
    public class Builder
    {
        public Builder SetA() { return this; }
        public Builder SetB() { return this; }
        public void Build() { }
        public void Run() { SetA().SetB().Build(); }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Equal(3, edges.Count);
        Assert.All(edges, e => Assert.Contains("Run", e.CallerId));
    }

    [Fact]
    public void HandlesExpressionBodiedMember()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public int GetValue() => Compute();
        private int Compute() { return 42; }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Single(edges);
        Assert.Contains("GetValue", edges[0].CallerId);
        Assert.Contains("Compute", edges[0].CalleeId);
    }

    [Fact]
    public void EmptyWorkspaceReturnsNoEdges()
    {
        var source = @"
namespace MyApp
{
    public class Empty { }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Empty(edges);
    }

    [Fact]
    public void HandlesImplicitObjectCreation()
    {
        var source = @"
namespace MyApp
{
    public class Widget
    {
        public Widget(int x) { }
    }
    public class Factory
    {
        public Widget Create() { Widget w = new(5); return w; }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Single(edges);
        Assert.Equal(CallKind.Constructor, edges[0].Kind);
        Assert.Contains("Create", edges[0].CallerId);
    }

    [Fact]
    public void HandlesCrossClassCalls()
    {
        var source = @"
namespace MyApp
{
    public class ServiceA
    {
        public ServiceA(int id) { }
        public void DoWork() { }
    }
    public class ServiceB
    {
        public void Run()
        {
            var a = new ServiceA(1);
            a.DoWork();
        }
    }
}";
        var workspace = TestHelpers.CreateWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, e => e.Kind == CallKind.Constructor);
        Assert.Contains(edges, e => e.Kind == CallKind.Direct && e.CalleeId.Contains("DoWork"));
    }

    [Fact]
    public void DetectsCallsFromTopLevelStatements()
    {
        var source = @"
MyApp.Service.DoWork();

namespace MyApp
{
    public class Service
    {
        public static void DoWork() { }
    }
}";
        var workspace = TestHelpers.CreateConsoleWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        Assert.Contains(edges, e =>
            e.CallerId == CallGraphBuilder.TopLevelEntryPointId &&
            e.CalleeId.Contains("DoWork"));
    }

    [Fact]
    public void DetectsLocalFunctionsInTopLevelStatements()
    {
        var source = @"
Helper();

void Helper()
{
    MyApp.Service.DoWork();
}

namespace MyApp
{
    public class Service
    {
        public static void DoWork() { }
    }
}";
        var workspace = TestHelpers.CreateConsoleWorkspace(source);
        var edges = _builder.BuildCallGraph(workspace);

        // Top-level statements call Helper
        Assert.Contains(edges, e =>
            e.CallerId == CallGraphBuilder.TopLevelEntryPointId &&
            e.CalleeId.Contains("Helper"));
        // Helper calls DoWork
        Assert.Contains(edges, e =>
            e.CallerId.Contains("Helper") &&
            e.CalleeId.Contains("DoWork"));
    }
}
