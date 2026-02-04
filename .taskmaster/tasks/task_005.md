# Task ID: 5

**Title:** Implement Call Graph Builder

**Status:** done

**Dependencies:** 2 ✓, 3 ✓

**Priority:** high

**Description:** Build the method-level call graph by analyzing method bodies using Roslyn's semantic model to resolve invocations, including interface dispatch and virtual calls.

**Details:**

1. Create `CallGraphBuilder` class:
   ```csharp
   public class CallGraphBuilder
   {
       public List<MethodCallEdge> BuildCallGraph(LoadedWorkspace workspace, List<ProjectModel> codeModel)
       {
           // For each method in the code model:
           // 1. Get the SyntaxNode for the method declaration
           // 2. Find all InvocationExpressionSyntax nodes in the body
           // 3. Use SemanticModel.GetSymbolInfo() to resolve the target
           // 4. Map resolved IMethodSymbol back to our MethodModel IDs
           // 5. Handle interface dispatch: if target is interface method,
           //    also add edges to known implementations
       }
   }
   ```
2. Create `MethodCallEdge` record: `(string CallerId, string CalleeId, CallKind Kind)`
3. Handle call kinds:
   - Direct method call
   - Virtual/override dispatch
   - Interface method call
   - Delegate invocation
   - Constructor calls
4. Resolve `ObjectCreationExpression` as constructor calls
5. Handle LINQ expressions and lambda invocations
6. Skip calls to external (non-solution) methods but log them
7. Build interface→implementation mapping from TypeImplements data
8. Support bidirectional traversal: callers and callees

**Test Strategy:**

Create test fixtures with: direct calls, interface dispatch, virtual calls, constructor calls, LINQ chains, lambda expressions. Verify all edges are correctly captured. Test that interface calls resolve to implementations. Verify no duplicate edges. Test with circular call patterns.

## Subtasks

### 5.1. Create MethodCallEdge model and CallGraphBuilder class skeleton

**Status:** done  
**Dependencies:** None  

Define the MethodCallEdge record type with CallKind enum and create the CallGraphBuilder class with its public API signature and helper method stubs.

**Details:**

1. Create `CallKind` enum with values: Direct, Virtual, Interface, Delegate, Constructor, Extension, Operator.
2. Create `MethodCallEdge` record: `(string CallerId, string CalleeId, CallKind Kind, string? Location)` where Location captures file/line for diagnostics.
3. Create `CallGraphBuilder` class with:
   - `public List<MethodCallEdge> BuildCallGraph(LoadedWorkspace workspace, List<ProjectModel> codeModel)` as main entry point
   - Private helper stubs for each resolution strategy
   - A `Dictionary<string, List<string>>` for interface-to-implementations mapping
   - A `HashSet<MethodCallEdge>` to deduplicate edges
   - Logging for skipped external method calls
4. Create `CallGraphResult` wrapper class with helper methods: `GetCallers(string methodId)`, `GetCallees(string methodId)` for bidirectional traversal.
5. Place all types in a `CallGraph` namespace/folder within the Core project.

### 5.2. Implement syntax node location for method declarations

**Status:** done  
**Dependencies:** 5.1  

Map each MethodModel from the code model back to its corresponding SyntaxNode (MethodDeclarationSyntax, ConstructorDeclarationSyntax, etc.) using the Roslyn SemanticModel to enable body analysis.

**Details:**

1. For each project in the code model, get the corresponding `Compilation` from LoadedWorkspace.
2. For each MethodModel, reconstruct the lookup path: use the fully-qualified name and parameter types to locate the `IMethodSymbol` via `Compilation.GetTypeByMetadataName()` then `.GetMembers()`.
3. Handle all method-like declarations: `MethodDeclarationSyntax`, `ConstructorDeclarationSyntax`, `DestructorDeclarationSyntax`, `OperatorDeclarationSyntax`, `ConversionOperatorDeclarationSyntax`, `AccessorDeclarationSyntax` (property getters/setters), and `LocalFunctionStatementSyntax`.
4. Handle expression-bodied members (`ArrowExpressionClauseSyntax`) which don't have a Block body but still contain invocable expressions.
5. Handle partial methods by combining declarations across syntax trees.
6. Cache the mapping `Dictionary<string, (IMethodSymbol Symbol, SyntaxNode Node, SemanticModel Model)>` for reuse during invocation resolution.
7. Handle generic method instantiations by mapping back to the `OriginalDefinition` or `ReducedFrom` symbol.
8. Log warnings for MethodModels that cannot be resolved (e.g., generated code, missing references).

### 5.3. Implement InvocationExpression resolution for direct method calls

**Status:** done  
**Dependencies:** 5.2  

Walk method bodies to find InvocationExpressionSyntax nodes, use SemanticModel.GetSymbolInfo() to resolve targets, and map resolved IMethodSymbols back to MethodModel IDs for direct call edges.

**Details:**

1. For each resolved method SyntaxNode, get all descendant `InvocationExpressionSyntax` nodes from the body (Block or ArrowExpressionClause).
2. For each invocation, call `semanticModel.GetSymbolInfo(invocation)` to get the resolved symbol.
3. Handle `SymbolInfo.Symbol` (resolved) vs `SymbolInfo.CandidateSymbols` (ambiguous/overloaded) - for candidates, add edges to all candidate methods within the solution.
4. Map the resolved `IMethodSymbol` back to a MethodModel ID:
   - Use `OriginalDefinition` to handle generic instantiations (e.g., `List<int>.Add` → `List<T>.Add`)
   - Use `ReducedFrom` to handle extension method calls resolved to their static form
   - Build a reverse lookup `Dictionary<ISymbol, string>` using SymbolEqualityComparer.Default
5. Skip methods where the resolved symbol belongs to assemblies outside the solution (external calls) - log these at Debug level.
6. Create edges with `CallKind.Direct` for standard resolved invocations.
7. Handle chained method calls (fluent APIs) where each `.Method()` in a chain is a separate invocation.
8. Handle `nameof()` expressions - these are NOT invocations and should be skipped.

### 5.4. Handle ObjectCreationExpression as constructor calls and member access patterns

**Status:** done  
**Dependencies:** 5.2  

Resolve ObjectCreationExpression (new T()), ImplicitObjectCreationExpression (new()), and base/this constructor initializers as constructor call edges.

**Details:**

1. Find all `ObjectCreationExpressionSyntax` nodes in method bodies.
2. Use `semanticModel.GetSymbolInfo(objectCreation)` to resolve the constructor `IMethodSymbol`.
3. Handle `ImplicitObjectCreationExpressionSyntax` (target-typed `new()`) which requires type inference from context.
4. Handle `BaseConstructorInitializerSyntax` (`: base(...)`) and `ThisConstructorInitializerSyntax` (`: this(...)`) in constructor declarations.
5. Create edges with `CallKind.Constructor`.
6. Handle object initializer expressions - property setters in `{ Prop = value }` may invoke property setters which are method-like.
7. Handle collection initializer expressions which invoke `.Add()` methods implicitly.
8. Handle array creation expressions (`new int[] { ... }`) - these don't invoke constructors but should be recognized and skipped.
9. Map resolved constructor IMethodSymbols back to MethodModel IDs using the same reverse lookup from subtask 3.
10. Handle `Activator.CreateInstance<T>()` and similar reflection-based construction - log as unresolvable.

### 5.5. Implement interface dispatch resolution

**Status:** done  
**Dependencies:** 5.3  

Build an interface-to-implementation mapping from the code model's TypeImplements data and resolve interface method calls to all known implementing methods.

**Details:**

1. Pre-build an interface implementation map from the code model:
   - For each TypeModel with `ImplementedInterfaces`, map interface method IDs to the implementing type's corresponding method IDs.
   - Handle explicit interface implementations (e.g., `IFoo.Bar()`) which have different naming patterns.
   - Handle implicit interface implementations where the method name matches.
2. When an invocation resolves to an `IMethodSymbol` where `ContainingType.TypeKind == TypeKind.Interface`:
   - Create an edge with `CallKind.Interface` to the interface method itself.
   - Look up all known implementations from the pre-built map.
   - Create additional edges with `CallKind.Interface` to each implementation.
3. Handle generic interface implementations (e.g., `IComparable<T>`) by matching on the unbound generic interface.
4. Handle interface inheritance chains (e.g., `IFoo : IBar` where IBar.Method is called but implemented in a class implementing IFoo).
5. Handle cases where the implementation map is incomplete (abstract classes implementing only some interface methods) - log these.
6. Store the interface map in CallGraphBuilder for reuse across all method bodies.

### 5.6. Handle virtual/override dispatch and delegate invocations

**Status:** done  
**Dependencies:** 5.3  

Resolve virtual and override method calls to all possible dispatch targets in the type hierarchy, and handle delegate/event invocations including Func<>/Action<> types.

**Details:**

1. **Virtual/Override dispatch:**
   - When invocation resolves to a virtual/abstract method (`IMethodSymbol.IsVirtual || IsAbstract || IsOverride`):
     - Create edge with `CallKind.Virtual` to the declared method.
     - Walk the type hierarchy to find all overrides: use `IMethodSymbol.OverriddenMethod` chain upward and find all types that override it downward.
     - Build override lookup: for each type in the solution, check if it overrides the target method.
     - Create edges to each override with `CallKind.Virtual`.
   - Handle `base.Method()` calls which bypass virtual dispatch - these should be `CallKind.Direct` to the specific base implementation.
2. **Delegate invocations:**
   - Detect delegate invocations: `delegateVariable(args)` or `delegateVariable.Invoke(args)` patterns.
   - For typed delegates (Func<>, Action<>, custom delegate types), the target is generally unknown at static analysis time.
   - When the delegate is assigned from a method group in the same method/class, resolve the target.
   - Handle event invocations (`EventName?.Invoke(...)`) - mark as `CallKind.Delegate`.
   - Track delegate assignments via `+=` to identify potential targets for events.
3. Handle `sealed` override methods - these don't need further dispatch resolution.

### 5.7. Handle LINQ expressions, lambda invocations, and edge cases

**Status:** done  
**Dependencies:** 5.3, 5.4, 5.5, 5.6  

Resolve LINQ method chain calls (Select, Where, etc.), lambda/anonymous method invocations, extension methods, operator overloads, and conditional access patterns (?.).

**Details:**

1. **LINQ and extension methods:**
   - LINQ query syntax (`from x in y select z`) is lowered to method calls - find the underlying `InvocationExpressionSyntax` nodes generated by the compiler or use `GetSymbolInfo()` on query clause syntax nodes.
   - LINQ method syntax (`.Where().Select()`) is already handled as chained invocations, but ensure extension method resolution uses `ReducedFrom` to map back to the static extension method definition.
   - Handle custom extension methods defined in the solution.
2. **Lambda and anonymous methods:**
   - Lambdas passed as arguments (e.g., `.Where(x => x.IsValid())`) contain invocations within their bodies.
   - Recursively walk lambda bodies (`LambdaExpressionSyntax`, `AnonymousMethodExpressionSyntax`) for invocations.
   - Attribute the discovered calls to the containing method (the lambda's enclosing method).
3. **Conditional access (`?.`):**
   - `ConditionalAccessExpressionSyntax` wraps invocations differently: the method call is inside a `MemberBindingExpressionSyntax`.
   - Use `GetSymbolInfo()` on the overall conditional access to resolve the target.
4. **Operator overloads:**
   - Binary/unary operator expressions may resolve to user-defined operator methods.
   - Check `semanticModel.GetSymbolInfo(binaryExpression)` for operator overloads defined in solution types.
   - Create edges with `CallKind.Operator` (add to CallKind enum if needed, otherwise use Direct).
5. **Implicit conversions:**
   - User-defined implicit/explicit conversion operators invoked implicitly in assignments or casts.
   - Use `GetConversion()` or `GetSymbolInfo()` on cast expressions.
6. **Pattern matching invocations:** Handle `is` patterns that invoke `Deconstruct` methods.
