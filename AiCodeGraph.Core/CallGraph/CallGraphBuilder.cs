using AiCodeGraph.Core.Models;
using AiCodeGraph.Core.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Core.CallGraph;

public class CallGraphBuilder
{
    private readonly HashSet<MethodCallEdge> _edges = new();
    private readonly Dictionary<ISymbol, string> _symbolToId = new(SymbolEqualityComparer.Default);

    public List<MethodCallEdge> BuildCallGraph(LoadedWorkspace workspace)
    {
        _edges.Clear();
        _symbolToId.Clear();

        // First pass: build symbol-to-ID mapping for all methods in the solution
        foreach (var (projectId, compilation) in workspace.Compilations)
        {
            BuildSymbolMap(compilation);
        }

        // Second pass: walk method bodies and resolve invocations
        foreach (var (projectId, compilation) in workspace.Compilations)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                WalkMethodBodies(root, semanticModel);
            }
        }

        return _edges.ToList();
    }

    private void BuildSymbolMap(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var methodDecl in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                if (symbol != null && !_symbolToId.ContainsKey(symbol))
                {
                    _symbolToId[symbol] = SymbolIdGenerator.GenerateId(symbol);
                    if (!SymbolEqualityComparer.Default.Equals(symbol, symbol.OriginalDefinition))
                        _symbolToId[symbol.OriginalDefinition] = SymbolIdGenerator.GenerateId(symbol.OriginalDefinition);
                }
            }

            foreach (var localFunc in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(localFunc);
                if (symbol != null && !_symbolToId.ContainsKey(symbol))
                    _symbolToId[symbol] = SymbolIdGenerator.GenerateId(symbol);
            }
        }
    }

    private void WalkMethodBodies(SyntaxNode root, SemanticModel semanticModel)
    {
        foreach (var methodDecl in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            var callerSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
            if (callerSymbol == null) continue;

            var callerId = GetSymbolId(callerSymbol);
            if (callerId == null) continue;

            // Walk the body (block or expression body)
            var body = MethodBodyHelper.GetMethodBody(methodDecl);

            if (body == null) continue;

            // Handle constructor initializer (base/this calls)
            if (methodDecl is ConstructorDeclarationSyntax ctor && ctor.Initializer != null)
            {
                var initSymbol = semanticModel.GetSymbolInfo(ctor.Initializer).Symbol as IMethodSymbol;
                if (initSymbol != null)
                    AddEdge(callerId, initSymbol, CallKind.Constructor);
            }

            ResolveInvocationsInBody(callerId, body, semanticModel);
        }
    }

    private void ResolveInvocationsInBody(string callerId, SyntaxNode body, SemanticModel semanticModel)
    {
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    ResolveInvocation(callerId, invocation, semanticModel);
                    break;

                case ObjectCreationExpressionSyntax creation:
                    ResolveObjectCreation(callerId, creation, semanticModel);
                    break;

                case ImplicitObjectCreationExpressionSyntax implicitCreation:
                    ResolveImplicitObjectCreation(callerId, implicitCreation, semanticModel);
                    break;
            }
        }
    }

    private void ResolveInvocation(string callerId, InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);

        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            var kind = DetermineCallKind(method);
            AddEdge(callerId, method, kind);
        }
        else if (symbolInfo.CandidateSymbols.Length > 0)
        {
            foreach (var candidate in symbolInfo.CandidateSymbols.OfType<IMethodSymbol>())
                AddEdge(callerId, candidate, CallKind.Direct);
        }
    }

    private void ResolveObjectCreation(string callerId, ObjectCreationExpressionSyntax creation, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(creation);
        if (symbolInfo.Symbol is IMethodSymbol ctor)
            AddEdge(callerId, ctor, CallKind.Constructor);
    }

    private void ResolveImplicitObjectCreation(string callerId, ImplicitObjectCreationExpressionSyntax creation, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(creation);
        if (symbolInfo.Symbol is IMethodSymbol ctor)
            AddEdge(callerId, ctor, CallKind.Constructor);
    }

    private CallKind DetermineCallKind(IMethodSymbol method)
    {
        if (method.MethodKind == MethodKind.Constructor)
            return CallKind.Constructor;

        if (method.IsExtensionMethod || method.ReducedFrom != null)
            return CallKind.Extension;

        if (method.ContainingType?.TypeKind == TypeKind.Interface)
            return CallKind.Interface;

        if (method.IsVirtual || method.IsAbstract || method.IsOverride)
            return CallKind.Virtual;

        return CallKind.Direct;
    }

    private void AddEdge(string callerId, IMethodSymbol targetMethod, CallKind kind)
    {
        var resolved = targetMethod.OriginalDefinition;
        if (targetMethod.ReducedFrom != null)
            resolved = targetMethod.ReducedFrom.OriginalDefinition;

        var calleeId = GetSymbolId(resolved);
        if (calleeId != null && callerId != calleeId)
            _edges.Add(new MethodCallEdge(callerId, calleeId, kind));
    }

    private string? GetSymbolId(ISymbol symbol)
    {
        if (_symbolToId.TryGetValue(symbol, out var id))
            return id;

        var original = symbol.OriginalDefinition;
        if (_symbolToId.TryGetValue(original, out id))
            return id;

        return null;
    }
}
