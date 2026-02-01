# Task ID: 25

**Title:** Extract Shared GetMethodBody Utility

**Status:** done

**Dependencies:** None

**Priority:** medium

**Description:** Create MethodBodyHelper in a Shared directory with a single GetMethodBody() method, removing duplication from MetricsEngine, CallGraphBuilder, and IntentNormalizer.

**Details:**

Create new file: AiCodeGraph.Core/Shared/MethodBodyHelper.cs

```csharp
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;

namespace AiCodeGraph.Core.Shared;

public static class MethodBodyHelper
{
    public static SyntaxNode? GetMethodBody(BaseMethodDeclarationSyntax methodDecl)
    {
        // Return body block if present (regular methods)
        if (methodDecl.Body != null)
            return methodDecl.Body;
        
        // Return expression body if present (arrow expression methods)
        if (methodDecl.ExpressionBody != null)
            return methodDecl.ExpressionBody;
        
        return null;
    }
}
```

Modify these files to use the helper:
1. `AiCodeGraph.Core/Metrics/MetricsEngine.cs` line 59 - replace inline body extraction
2. `AiCodeGraph.Core/CallGraph/CallGraphBuilder.cs` line 76 - replace inline body extraction  
3. `AiCodeGraph.Core/Normalization/IntentNormalizer.cs` line 48 - replace inline body extraction

Each replacement changes the inline logic to:
```csharp
var body = MethodBodyHelper.GetMethodBody(methodDecl);
if (body == null) continue; // or return, depending on context
```

Create the Shared directory: `mkdir -p AiCodeGraph.Core/Shared/`

**Test Strategy:**

Create AiCodeGraph.Tests/MethodBodyHelperTests.cs with tests: (1) Method with block body returns BlockSyntax. (2) Method with expression body returns ArrowExpressionClauseSyntax. (3) Abstract method (no body) returns null. (4) Constructor with body works. (5) All existing MetricsEngine, CallGraph, and Normalization tests pass unchanged.

## Subtasks

### 25.1. Create MethodBodyHelper static class in Shared directory

**Status:** pending  
**Dependencies:** None  

Create the AiCodeGraph.Core/Shared/ directory and implement MethodBodyHelper.cs with a static GetMethodBody method that extracts the body (block or expression) from any BaseMethodDeclarationSyntax node.

**Details:**

1. Create directory AiCodeGraph.Core/Shared/
2. Create MethodBodyHelper.cs with namespace AiCodeGraph.Core.Shared
3. Implement static method GetMethodBody(BaseMethodDeclarationSyntax methodDecl) that returns SyntaxNode? - checking Body first, then ExpressionBody, returning null if neither exists
4. This handles all BaseMethodDeclarationSyntax subtypes: MethodDeclarationSyntax, ConstructorDeclarationSyntax, DestructorDeclarationSyntax, OperatorDeclarationSyntax, ConversionOperatorDeclarationSyntax

### 25.2. Update MetricsEngine, CallGraphBuilder, and IntentNormalizer to use MethodBodyHelper

**Status:** pending  
**Dependencies:** 25.1  

Replace the inline body extraction logic in MetricsEngine.cs (line 59), CallGraphBuilder.cs (line 76), and IntentNormalizer.cs (line 48) with calls to MethodBodyHelper.GetMethodBody, adding the appropriate using directive to each file.

**Details:**

1. In MetricsEngine.cs (~line 59): Replace the inline switch/if logic that extracts body from BaseMethodDeclarationSyntax with `var body = MethodBodyHelper.GetMethodBody(methodDecl); if (body == null) continue;` and add `using AiCodeGraph.Core.Shared;`
2. In CallGraphBuilder.cs (~line 76): Same replacement pattern - replace inline body extraction with MethodBodyHelper.GetMethodBody call, add using directive
3. In IntentNormalizer.cs (~line 48): Same replacement pattern - replace inline body extraction with MethodBodyHelper.GetMethodBody call, add using directive
4. Note: MetricsEngine also has a LocalFunctionStatementSyntax variant that is NOT covered by this helper - leave that logic in place
5. Run `dotnet build` to verify compilation and `dotnet test` to verify all 178 existing tests pass unchanged
