# Task ID: 6

**Title:** Implement Cognitive Complexity Metrics Engine

**Status:** done

**Dependencies:** 2 ✓, 3 ✓

**Priority:** high

**Description:** Compute cognitive complexity, lines of code, and nesting depth for each method. Cognitive complexity follows the Sonar-style algorithm measuring how difficult code is to understand.

**Details:**

1. Create `CognitiveComplexityCalculator`:
   ```csharp
   public class CognitiveComplexityCalculator : CSharpSyntaxWalker
   {
       private int _complexity = 0;
       private int _nestingLevel = 0;
       
       // Increment for:
       // +1 for each: if, else if, else, switch, for, foreach, while, do, catch, goto, &&, ||
       // +1 for each: break/continue to label, recursion
       // +nesting for: nested if/for/while/switch/catch/lambda
       // No increment for: method declaration, sequential statements
   }
   ```
2. Create `MetricsEngine` class:
   ```csharp
   public record MethodMetrics(string MethodId, int CognitiveComplexity, int LinesOfCode, int MaxNestingDepth);
   public List<MethodMetrics> ComputeMetrics(LoadedWorkspace workspace, List<MethodModel> methods);
   ```
3. Cognitive complexity rules:
   - +1 for control flow breaks: if, else if, else, switch/case, for, foreach, while, do-while, catch, goto, ternary (?:)
   - +1 for logical operators that change context: &&, ||
   - +1 for each break/continue to a label
   - +nesting increment for nested structures
   - Nesting incremented by: if, else if, else, switch, for, foreach, while, do, catch, lambda, local function
4. Lines of Code: count non-empty, non-comment lines in method body
5. Max Nesting Depth: track deepest control structure nesting
6. Handle expression-bodied members (=> syntax)
7. Handle switch expressions vs switch statements

**Test Strategy:**

Create test methods with known complexity scores (reference SonarSource examples). Test: simple sequential method (score 0-1), single if (score 1), nested ifs (increasing score), loops with conditions, try-catch blocks, LINQ chains. Verify LOC counting excludes comments and blank lines. Compare results against SonarQube reference implementation.

## Subtasks

### 6.1. Implement CognitiveComplexityCalculator with Nesting Level Tracking

**Status:** done  
**Dependencies:** None  

Create the CognitiveComplexityCalculator class extending CSharpSyntaxWalker with nesting level tracking infrastructure. Implement the visitor pattern skeleton with _complexity and _nestingLevel fields, entry/exit methods for nesting-incrementing constructs (if, else if, else, switch, for, foreach, while, do, catch, lambda, local function), and the public API to compute complexity for a given method syntax node.

**Details:**

Create `CognitiveComplexityCalculator : CSharpSyntaxWalker` in the Core/Metrics directory. Include:
- Private fields: `_complexity` (int), `_nestingLevel` (int)
- Public method: `int Calculate(MethodDeclarationSyntax node)` and overload for `LocalFunctionStatementSyntax`
- Helper method `IncrementWithNesting()` that adds `1 + _nestingLevel` to complexity
- Helper method `IncrementWithoutNesting()` that adds just `1` to complexity
- Nesting management: override `Visit*` methods for nesting-incrementing constructs, incrementing `_nestingLevel` before visiting children and decrementing after
- Handle the distinction between `if` and `else if`: when an `else` clause contains a single `if` statement, treat it as `else if` (no additional nesting increment)
- Reference SonarSource cognitive complexity specification for correct nesting behavior
- Reset state between calculations to allow reuse of the calculator instance

### 6.2. Implement Base Increment Rules for Control Flow and Logical Operators

**Status:** done  
**Dependencies:** 6.1  

Implement the +1 base increment rules in the CognitiveComplexityCalculator for all control flow breaks and logical operators: if, else if, else, switch/case, for, foreach, while, do-while, catch, goto, ternary (?:), &&, ||, break/continue to label, and null-coalescing (??). Each of these adds exactly +1 to complexity regardless of nesting level.

**Details:**

Override the following visitor methods in CognitiveComplexityCalculator:
- `VisitIfStatement`: +1 (base increment, not nesting increment here - nesting handled separately)
- `VisitElseClause`: +1 for `else`, but if the else contains only an `if`, treat as `else if` (+1 for the if, no extra nesting)
- `VisitSwitchStatement`: +1
- `VisitForStatement`, `VisitForEachStatement`: +1 each
- `VisitWhileStatement`, `VisitDoStatement`: +1 each
- `VisitCatchClause`: +1
- `VisitGotoStatement`: +1
- `VisitConditionalExpression` (ternary ?:): +1
- `VisitBinaryExpression`: +1 for `&&` (LogicalAndExpression) and `||` (LogicalOrExpression), but only count sequences of the same operator once per change in operator type (e.g., `a && b && c` = +1, `a && b || c` = +2)
- `VisitBreakStatement`/`VisitContinueStatement`: +1 only when targeting a label
- Handle `??` (CoalesceExpression): +1 per SonarSource rules
- Handle pattern matching `is` expressions and switch expressions as appropriate

### 6.3. Implement Nesting Increment Rules for Nested Structures

**Status:** done  
**Dependencies:** 6.1, 6.2  

Implement the nesting-based increment rules where nested control structures add +1 for the base plus the current nesting level. Structures that increment nesting include: if, else if, else, switch, for, foreach, while, do, catch, lambda expressions, and local functions.

**Details:**

Modify the visitor methods to apply the nesting increment pattern:
- When entering a nesting-incrementing construct, the complexity added is `1 + _nestingLevel` (base +1, plus nesting bonus)
- After adding complexity, increment `_nestingLevel` before visiting child nodes, then decrement after
- Nesting-incrementing constructs: if, else if, else, switch, for, foreach, while, do, catch
- Lambda expressions (`SimpleLambdaExpression`, `ParenthesizedLambdaExpression`): increment nesting but do NOT add base +1 for the lambda itself
- Local functions (`LocalFunctionStatement`): increment nesting but do NOT add base +1 for the declaration itself (they reset nesting context in some interpretations - follow SonarSource spec)
- `else if` special case: the `if` inside an `else` should NOT increment nesting (it's at the same conceptual level as the parent if)
- Ternary expressions nested inside other structures should receive nesting increment
- Handle deeply nested structures (3+ levels) correctly: if { if { if {} } } should produce 1 + 2 + 3 = 6

### 6.4. Implement LinesOfCode Counter and MaxNestingDepth Tracker

**Status:** done  
**Dependencies:** None  

Implement two utility metric calculators: a LinesOfCode counter that counts non-empty, non-comment lines within a method body, and a MaxNestingDepth tracker that determines the deepest control structure nesting level within a method.

**Details:**

Create two calculator classes or static methods:

**LinesOfCodeCalculator:**
- Accept a `MethodDeclarationSyntax` or `BaseMethodDeclarationSyntax` node
- Get the full text of the method body (or expression body for `=>`)
- Split into lines and count lines that are:
  - Not empty/whitespace-only
  - Not single-line comments (`//`)
  - Not part of multi-line comments (`/* */`)
  - Not XML doc comments (`///`)
- Handle mixed lines (code + trailing comment): count as code line
- Handle expression-bodied members: count the expression as 1+ lines
- Use Roslyn trivia API to identify comment trivia rather than string parsing for accuracy

**MaxNestingDepthCalculator (CSharpSyntaxWalker):**
- Track `_currentDepth` and `_maxDepth`
- Increment depth when entering: if, else, for, foreach, while, do, switch, try, catch, finally, lock, using statement
- Record max depth as `Math.Max(_currentDepth, _maxDepth)`
- Decrement depth when exiting
- Handle nested lambdas and local functions (they contribute to nesting)
- Expression-bodied members have depth 0 (no block nesting)

### 6.5. Create MetricsEngine Class with Expression-Bodied and Switch Expression Handling

**Status:** done  
**Dependencies:** 6.1, 6.2, 6.3, 6.4  

Create the MetricsEngine class that orchestrates all metric calculations, computing CognitiveComplexity, LinesOfCode, and MaxNestingDepth for a list of methods. Handle special cases including expression-bodied members (=> syntax) and switch expressions vs switch statements.

**Details:**

Create `MetricsEngine` class:
```csharp
public record MethodMetrics(string MethodId, int CognitiveComplexity, int LinesOfCode, int MaxNestingDepth);

public class MetricsEngine
{
    public List<MethodMetrics> ComputeMetrics(LoadedWorkspace workspace, List<MethodModel> methods);
}
```

Implementation details:
- For each `MethodModel`, locate the corresponding `SyntaxNode` from the workspace's compilation
- Use the `MethodModel.Id` (stable symbol ID from Task 3) to find the syntax node via semantic model
- Instantiate and run all three calculators: CognitiveComplexityCalculator, LinesOfCodeCalculator, MaxNestingDepthCalculator
- **Expression-bodied members** (`=>`): These have no block body; treat the expression as the body. Cognitive complexity should still analyze the expression (may contain ternary, null-coalescing, etc.)
- **Switch expressions** (`x switch { pattern => value, ... }`): Each arm adds +1 complexity (similar to case), and the switch expression itself adds +1. Nesting applies if inside another structure.
- **Switch statements**: Traditional +1 per case with nesting
- Handle `ArrowExpressionClauseSyntax` for expression-bodied members
- Handle methods that can't be found (removed between extraction and analysis): skip with warning
- Return results as a list of `MethodMetrics` records
- Consider parallel computation for large codebases using `Parallel.ForEach` or async patterns
