using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Core.Metrics;

public class CognitiveComplexityCalculator : CSharpSyntaxWalker
{
    private int _complexity;
    private int _nestingLevel;

    public int Calculate(SyntaxNode methodBody)
    {
        _complexity = 0;
        _nestingLevel = 0;
        Visit(methodBody);
        return _complexity;
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        // Only increment if this is NOT an "else if" (handled in VisitElseClause)
        if (node.Parent is not ElseClauseSyntax)
        {
            _complexity += 1 + _nestingLevel;
            _nestingLevel++;
            Visit(node.Condition);
            Visit(node.Statement);
            _nestingLevel--;
        }
        else
        {
            // This is an "else if" - base +1 only, no nesting increment
            _complexity += 1;
            Visit(node.Condition);
            _nestingLevel++;
            Visit(node.Statement);
            _nestingLevel--;
        }

        if (node.Else != null)
            Visit(node.Else);
    }

    public override void VisitElseClause(ElseClauseSyntax node)
    {
        if (node.Statement is IfStatementSyntax ifStmt)
        {
            // "else if" - visit the if statement which handles its own increment
            Visit(ifStmt);
        }
        else
        {
            // Plain "else"
            _complexity += 1;
            Visit(node.Statement);
        }
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        _complexity += 1 + _nestingLevel;
        _nestingLevel++;
        base.VisitForStatement(node);
        _nestingLevel--;
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        _complexity += 1 + _nestingLevel;
        _nestingLevel++;
        base.VisitForEachStatement(node);
        _nestingLevel--;
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        _complexity += 1 + _nestingLevel;
        _nestingLevel++;
        base.VisitWhileStatement(node);
        _nestingLevel--;
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        _complexity += 1 + _nestingLevel;
        _nestingLevel++;
        base.VisitDoStatement(node);
        _nestingLevel--;
    }

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        _complexity += 1 + _nestingLevel;
        _nestingLevel++;
        base.VisitSwitchStatement(node);
        _nestingLevel--;
    }

    public override void VisitCatchClause(CatchClauseSyntax node)
    {
        _complexity += 1 + _nestingLevel;
        _nestingLevel++;
        base.VisitCatchClause(node);
        _nestingLevel--;
    }

    public override void VisitGotoStatement(GotoStatementSyntax node)
    {
        _complexity += 1;
        base.VisitGotoStatement(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        _complexity += 1 + _nestingLevel;
        _nestingLevel++;
        base.VisitConditionalExpression(node);
        _nestingLevel--;
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        // +1 for each sequence of logical operators that changes type
        // a && b && c = +1, a && b || c = +2
        if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
        {
            if (!IsPartOfSameLogicalSequence(node))
            {
                _complexity += 1;
            }
        }

        if (node.IsKind(SyntaxKind.CoalesceExpression))
        {
            if (!IsPartOfSameCoalesceSequence(node))
            {
                _complexity += 1;
            }
        }

        base.VisitBinaryExpression(node);
    }

    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        _complexity += 1 + _nestingLevel;
        _nestingLevel++;
        base.VisitSwitchExpression(node);
        _nestingLevel--;
    }

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        _nestingLevel++;
        base.VisitSimpleLambdaExpression(node);
        _nestingLevel--;
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        _nestingLevel++;
        base.VisitParenthesizedLambdaExpression(node);
        _nestingLevel--;
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        _nestingLevel++;
        base.VisitLocalFunctionStatement(node);
        _nestingLevel--;
    }

    private static bool IsPartOfSameLogicalSequence(BinaryExpressionSyntax node)
    {
        // If the left operand is the same kind of logical operation, this is a continuation
        if (node.Left is BinaryExpressionSyntax left)
        {
            if (left.IsKind(node.Kind()))
                return true;
        }
        return false;
    }

    private static bool IsPartOfSameCoalesceSequence(BinaryExpressionSyntax node)
    {
        if (node.Left is BinaryExpressionSyntax left)
        {
            if (left.IsKind(SyntaxKind.CoalesceExpression))
                return true;
        }
        return false;
    }
}
