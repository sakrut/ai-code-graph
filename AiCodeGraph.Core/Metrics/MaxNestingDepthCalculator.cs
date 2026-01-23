using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Core.Metrics;

public class MaxNestingDepthCalculator : CSharpSyntaxWalker
{
    private int _currentDepth;
    private int _maxDepth;

    public int Calculate(SyntaxNode? body)
    {
        if (body == null) return 0;

        _currentDepth = 0;
        _maxDepth = 0;
        Visit(body);
        return _maxDepth;
    }

    private void EnterNesting()
    {
        _currentDepth++;
        if (_currentDepth > _maxDepth)
            _maxDepth = _currentDepth;
    }

    private void ExitNesting()
    {
        _currentDepth--;
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        EnterNesting();
        base.VisitIfStatement(node);
        ExitNesting();
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        EnterNesting();
        base.VisitForStatement(node);
        ExitNesting();
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        EnterNesting();
        base.VisitForEachStatement(node);
        ExitNesting();
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        EnterNesting();
        base.VisitWhileStatement(node);
        ExitNesting();
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        EnterNesting();
        base.VisitDoStatement(node);
        ExitNesting();
    }

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        EnterNesting();
        base.VisitSwitchStatement(node);
        ExitNesting();
    }

    public override void VisitTryStatement(TryStatementSyntax node)
    {
        EnterNesting();
        base.VisitTryStatement(node);
        ExitNesting();
    }

    public override void VisitUsingStatement(UsingStatementSyntax node)
    {
        EnterNesting();
        base.VisitUsingStatement(node);
        ExitNesting();
    }

    public override void VisitLockStatement(LockStatementSyntax node)
    {
        EnterNesting();
        base.VisitLockStatement(node);
        ExitNesting();
    }

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        EnterNesting();
        base.VisitSimpleLambdaExpression(node);
        ExitNesting();
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        EnterNesting();
        base.VisitParenthesizedLambdaExpression(node);
        ExitNesting();
    }
}
