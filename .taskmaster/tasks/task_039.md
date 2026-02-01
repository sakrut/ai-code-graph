# Task ID: 39

**Title:** SymbolIdGenerator Comprehensive Tests

**Status:** done

**Dependencies:** None

**Priority:** medium

**Description:** Add comprehensive unit tests for SymbolIdGenerator.GetMethodId() covering all method types: simple, generic, overloaded, operators, constructors, nested types, and extension methods.

**Details:**

Create new file: AiCodeGraph.Tests/SymbolIdGeneratorTests.cs

```csharp
using AiCodeGraph.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace AiCodeGraph.Tests;

public class SymbolIdGeneratorTests
{
    private static IMethodSymbol GetMethodSymbol(string code, string methodName)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("Test")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);
        
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);
        
        return model.GetDeclaredSymbol(method)!;
    }
    
    [Fact]
    public void SimpleMethod_ReturnsFullyQualifiedId() { /* ... */ }
    
    [Fact]
    public void GenericMethod_IncludesTypeParameters() { /* ... */ }
    
    [Fact]
    public void OverloadedMethod_DistinguishesByParameters() { /* ... */ }
    
    [Fact]
    public void Constructor_IncludesCtor() { /* ... */ }
    
    [Fact]
    public void NestedType_IncludesOuterType() { /* ... */ }
    
    [Fact]
    public void ExtensionMethod_IncludesThisParameter() { /* ... */ }
    
    [Fact]
    public void OperatorOverload_IncludesOperator() { /* ... */ }
    
    [Fact]
    public void MultipleParameters_OrderPreserved() { /* ... */ }
}
```

Test each case by constructing a small Roslyn compilation, getting the IMethodSymbol, and calling SymbolIdGenerator.GetMethodId(). Verify the output format matches expectations.

**Test Strategy:**

Each test creates a minimal C# compilation with the relevant method pattern, obtains the IMethodSymbol via semantic model, and calls GetMethodId(). Assert the returned string matches expected format. Cover edge cases: nullable parameters, ref/out parameters, params arrays, default values.

## Subtasks

### 39.1. Set up test infrastructure with Roslyn compilation helper

**Status:** pending  
**Dependencies:** None  

Create the SymbolIdGeneratorTests.cs file with a reusable helper method that constructs a CSharpCompilation from source code, extracts an IMethodSymbol by name, and supports both MethodDeclarationSyntax and ConstructorDeclarationSyntax. Include proper MetadataReference for System.Runtime and System.Linq to support extension methods and generic types.

**Details:**

Create AiCodeGraph.Tests/SymbolIdGeneratorTests.cs with:
1. A private static helper `GetMethodSymbol(string code, string methodName)` that:
   - Parses code with CSharpSyntaxTree.ParseText()
   - Creates CSharpCompilation with OutputKind.DynamicallyLinkedLibrary
   - Adds references: typeof(object).Assembly.Location and any needed runtime refs
   - Gets SemanticModel, finds MethodDeclarationSyntax by Identifier.Text
   - Returns model.GetDeclaredSymbol(method) as IMethodSymbol
2. A second helper `GetConstructorSymbol(string code)` for constructors using ConstructorDeclarationSyntax
3. An `GetOperatorSymbol(string code, SyntaxKind operatorKind)` helper for operator overloads using OperatorDeclarationSyntax
4. Follow existing test conventions: namespace AiCodeGraph.Tests, xUnit [Fact] attributes, AAA pattern
5. Verify the helper works by adding a basic smoke test that calls SymbolIdGenerator.GenerateId() on a simple void method and asserts the result is non-empty

### 39.2. Write tests for basic method ID cases

**Status:** pending  
**Dependencies:** 39.1  

Implement unit tests covering simple methods, generic methods, overloaded methods, and constructors. Each test constructs a minimal C# source string, obtains the IMethodSymbol, calls SymbolIdGenerator.GenerateId(), and asserts the expected fully-qualified format with parameter types.

**Details:**

Add the following [Fact] tests to SymbolIdGeneratorTests:
1. SimpleMethod_ReturnsFullyQualifiedId - Test `void DoWork()` in namespace MyApp, class Service. Assert result contains 'MyApp.Service.DoWork()'.
2. GenericMethod_IncludesTypeParameters - Test `T GetValue<T>(T input)`. Assert result includes type parameter syntax like '<T>'.
3. OverloadedMethod_DistinguishesByParameters - Test two overloads: `void Process(int x)` and `void Process(string s)`. Call GetMethodSymbol for each, assert different IDs. Verify parameter types appear in the ID (int vs string).
4. Constructor_IncludesCtor - Test a constructor `public MyClass(int value)`. Use ConstructorDeclarationSyntax helper. Assert the ID contains '.MyClass(' or the ctor pattern from GenerateId's SymbolDisplayFormat.
5. MultipleParameters_OrderPreserved - Test `void Calculate(int a, string b, double c)`. Assert ID contains parameters in order: (int, string, double).

For each test, use inline C# source code as @"..." strings following the existing codebase pattern. Use Assert.Contains() for substring checks and Assert.Equal() where the exact format is known.

### 39.3. Write tests for advanced method ID cases

**Status:** pending  
**Dependencies:** 39.1  

Implement unit tests for edge cases: nested types, extension methods, operator overloads, ref/out parameters, and nullable parameters. These tests exercise less common but important IMethodSymbol scenarios that SymbolIdGenerator must handle correctly.

**Details:**

Add the following [Fact] tests to SymbolIdGeneratorTests:
1. NestedType_IncludesOuterType - Source with `class Outer { class Inner { void Work() {} } }`. Assert ID contains both 'Outer' and 'Inner' (e.g., 'Outer.Inner.Work()').
2. ExtensionMethod_IncludesThisParameter - Source with `static class Extensions { static void Extend(this string s) {} }`. Add System.Runtime reference. Assert the ID includes the parameter type.
3. OperatorOverload_IncludesOperator - Source with `public static MyClass operator +(MyClass a, MyClass b)`. Use OperatorDeclarationSyntax to get the symbol. Assert ID contains 'operator' or '+' indicator.
4. RefOutParameters_IncludesModifiers - Test `void Process(ref int x, out string y)`. Assert ID distinguishes ref/out parameters from regular ones (ref int vs int).
5. NullableParameter_IncludesNullability - Test `void Handle(string? name)` with nullable enabled. Assert the parameter type in the ID reflects nullability if the format supports it.
6. ParamsArray_IncludesArrayType - Test `void Log(params string[] messages)`. Assert ID shows string[] parameter type.

Use the IdFormat defined in SymbolIdGenerator to predict expected outputs. The format uses IncludeType for parameters and UseSpecialTypes, so expect 'int' not 'System.Int32'. For extension methods, add `using System;` and necessary references.
