# Task ID: 3

**Title:** Implement Code Model Extractor

**Status:** done

**Dependencies:** 2 ✓

**Priority:** high

**Description:** Extract the full structural hierarchy from Roslyn compilations: Projects → Namespaces → Types (classes, interfaces, records, structs) → Methods. Produce stable symbol IDs for each element.

**Details:**

1. Create model classes:
   ```csharp
   public record ProjectModel(string Id, string Name, string FilePath, List<NamespaceModel> Namespaces);
   public record NamespaceModel(string Id, string FullName, List<TypeModel> Types);
   public record TypeModel(string Id, string Name, string FullName, TypeKind Kind, List<MethodModel> Methods, List<string> ImplementedInterfaces);
   public record MethodModel(string Id, string Name, string FullName, string ReturnType, List<ParameterModel> Parameters, Location Location, int StartLine, int EndLine);
   ```
2. Create `CodeModelExtractor` class:
   - Walk each Compilation's global namespace recursively
   - Use `INamedTypeSymbol` for types, `IMethodSymbol` for methods
   - Generate stable IDs using `symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)`
   - Track type kind: class, interface, record, struct, enum
   - Extract method signatures, accessibility, modifiers
3. Handle partial classes (merge members)
4. Handle nested types
5. Skip compiler-generated members (check `IsImplicitlyDeclared`)
6. Extract `Contains` relationships (project→namespace→type→method)
7. Extract `Implements` relationships (type→interface)

**Test Strategy:**

Create test fixtures with various C# constructs: classes, interfaces, records, nested types, partial classes, generics. Verify extractor produces correct hierarchy. Verify stable IDs are consistent across runs. Verify interface implementation relationships are captured.

## Subtasks

### 3.1. Define Model Records with Stable ID Generation

**Status:** done  
**Dependencies:** None  

Create the core model record types (ProjectModel, NamespaceModel, TypeModel, MethodModel, ParameterModel) and a stable ID generation utility that uses Roslyn's SymbolDisplayFormat.FullyQualifiedFormat to produce consistent, unique identifiers across runs.

**Details:**

Create a Models directory in the Core project with the following records:

1. `ParameterModel(string Name, string Type, bool IsOptional, string? DefaultValue)` - represents method parameters
2. `MethodModel(string Id, string Name, string FullName, string ReturnType, List<ParameterModel> Parameters, string FilePath, int StartLine, int EndLine, Accessibility Accessibility, bool IsStatic, bool IsAsync, bool IsVirtual, bool IsOverride, bool IsAbstract)` - represents methods with full signature info
3. `TypeModel(string Id, string Name, string FullName, TypeKind Kind, List<MethodModel> Methods, List<string> ImplementedInterfaces, Accessibility Accessibility, bool IsStatic, bool IsAbstract, bool IsSealed, bool IsGeneric, List<string> TypeParameters, List<TypeModel> NestedTypes)` - represents types with nested type support
4. `NamespaceModel(string Id, string FullName, List<TypeModel> Types, List<NamespaceModel> ChildNamespaces)` - represents namespaces with hierarchy
5. `ProjectModel(string Id, string Name, string FilePath, List<NamespaceModel> Namespaces)` - represents projects
6. `TypeKind` enum: Class, Interface, Record, Struct, Enum, Delegate

Create a `SymbolIdGenerator` static utility class:
- `GenerateId(ISymbol symbol)` method using `symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` with SHA256 hash for URL-safe IDs
- Configure SymbolDisplayFormat to include type parameters, parameter types, and return types for method disambiguation
- Ensure generic types produce stable IDs regardless of whether they are open or closed (use the original definition)
- Add a `GenerateDisplayString(ISymbol symbol)` method for human-readable names

### 3.2. Implement Recursive Namespace Walking from GlobalNamespace

**Status:** done  
**Dependencies:** 3.1  

Create the CodeModelExtractor class with the core namespace traversal logic that recursively walks from Compilation.GlobalNamespace through all child namespaces, collecting type symbols at each level.

**Details:**

Create `CodeModelExtractor` class in Core project:

1. Main entry point: `ExtractProjectModel(Compilation compilation, string projectName, string projectFilePath) -> ProjectModel`
2. Implement `WalkNamespace(INamespaceSymbol namespaceSymbol) -> List<NamespaceModel>`:
   - Get all member namespaces via `namespaceSymbol.GetNamespaceMembers()`
   - Recursively walk child namespaces
   - Skip empty namespaces (no types and no child namespaces with types)
   - For the global namespace, don't create a NamespaceModel entry but process its children
   - Handle the case where types exist directly in the global namespace (no explicit namespace declaration)
3. Collect `INamedTypeSymbol` instances from `namespaceSymbol.GetTypeMembers()`
4. Filter out types from referenced assemblies - only include types from source (check `symbol.Locations` for `IsInSource`)
5. Build the namespace hierarchy maintaining parent-child relationships
6. Use CancellationToken support throughout for long-running operations
7. Consider using `INamespaceSymbol.ConstituentNamespaces` for merged namespace handling across partial declarations

### 3.3. Implement Type Extraction with Kind, Accessibility, and Modifiers

**Status:** done  
**Dependencies:** 3.1, 3.2  

Extract type information from INamedTypeSymbol including classification (class, interface, record, struct, enum, delegate), accessibility modifiers, static/abstract/sealed flags, and generic type parameter information.

**Details:**

Implement `ExtractType(INamedTypeSymbol typeSymbol) -> TypeModel` in CodeModelExtractor:

1. **Type Kind Detection:**
   - Use `typeSymbol.TypeKind` for basic classification (Class, Interface, Struct, Enum, Delegate)
   - For records: check `typeSymbol.IsRecord` property (available in Roslyn 4.x)
   - Map to the custom TypeKind enum

2. **Accessibility:**
   - Extract from `typeSymbol.DeclaredAccessibility` (Public, Internal, Private, Protected, ProtectedOrInternal, ProtectedAndInternal)

3. **Modifiers:**
   - `IsStatic`, `IsAbstract`, `IsSealed` from the symbol properties
   - Check `IsReadOnly` for readonly structs
   - Check `IsRefLikeType` for ref structs

4. **Generic Type Parameters:**
   - Check `typeSymbol.IsGenericType` and `typeSymbol.TypeParameters`
   - Extract type parameter names and constraints via `ITypeParameterSymbol.ConstraintTypes`
   - Store as List<string> of type parameter names

5. **Interface Implementation:**
   - Use `typeSymbol.AllInterfaces` to get all implemented interfaces (including inherited)
   - Or use `typeSymbol.Interfaces` for directly implemented only
   - Store as List<string> using the fully qualified display string
   - Distinguish between explicit and implicit interface implementations

6. **Source Location:**
   - Extract primary declaration location from `typeSymbol.Locations.First(l => l.IsInSource)`
   - Store file path, start line, end line

### 3.4. Implement Method Extraction with Signatures and Source Locations

**Status:** done  
**Dependencies:** 3.1, 3.2, 3.3  

Extract method information from IMethodSymbol including full signatures, parameters with types and defaults, return types, source locations (file path, start/end lines), and method modifiers (static, async, virtual, override, abstract).

**Details:**

Implement `ExtractMethod(IMethodSymbol methodSymbol) -> MethodModel` in CodeModelExtractor:

1. **Method Identification:**
   - Use `methodSymbol.Name` for simple name
   - Generate FullName using SymbolIdGenerator.GenerateDisplayString
   - Generate stable Id using SymbolIdGenerator.GenerateId
   - Handle special method names: constructors (.ctor), static constructors (.cctor), finalizers, operators

2. **Return Type:**
   - Use `methodSymbol.ReturnType.ToDisplayString()` with appropriate format
   - Handle void, Task<T>, ValueTask<T>, and nullable return types
   - For async methods, consider noting both the declared return type and the unwrapped type

3. **Parameters:**
   - Iterate `methodSymbol.Parameters`
   - For each parameter: name, type display string, IsOptional, default value (if HasExplicitDefaultValue)
   - Handle params arrays, ref/out/in modifiers
   - Handle generic type parameters in parameter types

4. **Source Location:**
   - Get from `methodSymbol.Locations.FirstOrDefault(l => l.IsInSource)`
   - Extract FileLinePositionSpan via `location.GetLineSpan()`
   - Store FilePath, StartLine (0-based from Roslyn, convert to 1-based), EndLine
   - For partial methods, get the implementation location

5. **Modifiers:**
   - IsStatic, IsAsync, IsVirtual, IsOverride, IsAbstract, IsSealed
   - IsExtensionMethod for static methods in static classes
   - Accessibility (Public, Private, Protected, Internal)

6. **Method Kinds to Include:**
   - Regular methods, constructors, property getters/setters (configurable), operators
   - Skip: compiler-generated accessors unless explicitly configured

### 3.5. Handle Edge Cases: Partial Classes, Nested Types, Compiler-Generated Members, and Generics

**Status:** done  
**Dependencies:** 3.2, 3.3, 3.4  

Implement robust handling of C# edge cases: merging partial class members from multiple declarations, recursively processing nested types, filtering compiler-generated/implicitly declared members, and correctly representing open and closed generic types.

**Details:**

Add edge case handling to CodeModelExtractor:

1. **Partial Classes:**
   - Roslyn's symbol API already merges partial declarations into a single INamedTypeSymbol
   - `typeSymbol.Locations` will contain multiple locations for partial types
   - Store all declaration locations (for UI display purposes)
   - `typeSymbol.DeclaringSyntaxReferences` gives all partial declarations
   - Members from all partial files are already unified in the symbol
   - Verify this works correctly with integration tests

2. **Nested Types:**
   - Check `typeSymbol.GetTypeMembers()` for nested types
   - Recursively call ExtractType for each nested type
   - Store in TypeModel.NestedTypes
   - Ensure IDs include the containing type (e.g., `Outer+Inner` format)
   - Handle deeply nested types (3+ levels)

3. **Compiler-Generated Members:**
   - Filter using `symbol.IsImplicitlyDeclared` - skip these entirely
   - Also check `symbol.CanBeReferencedByName` - skip if false
   - Skip backing fields for auto-properties (they have `IsImplicitlyDeclared = true`)
   - Skip record-generated members (Equals, GetHashCode, ToString, etc.) based on `IsImplicitlyDeclared`
   - Optionally skip property accessors (get_X, set_X) that are compiler-generated wrappers
   - Check for `[CompilerGenerated]` attribute as additional filter

4. **Generic Types:**
   - Use `typeSymbol.OriginalDefinition` for stable IDs of generic types
   - Handle open generics (List<T>) vs constructed generics (List<int>)
   - For IDs, always use the unbound/original definition
   - Extract type parameter constraints for display
   - Handle generic methods within generic types (multiple type parameter lists)

5. **Additional Edge Cases:**
   - Source-generated code: check if Location is in a GeneratedSourceText
   - Primary constructors (C# 12): included as constructor but parameters are also properties
   - File-scoped types: respect `file` accessibility modifier
   - Extension methods: mark on MethodModel for later relationship extraction

### 3.6. Extract Relationship Data: Contains Hierarchy and Implements Relationships

**Status:** done  
**Dependencies:** 3.3, 3.4, 3.5  

Build the structural Contains relationships (project→namespace→type→method) and Implements relationships (type→interface) as explicit relationship objects that can be stored and queried independently of the tree hierarchy.

**Details:**

Create relationship extraction in CodeModelExtractor:

1. **Relationship Model:**
   ```csharp
   public record Relationship(string SourceId, string TargetId, RelationshipKind Kind);
   public enum RelationshipKind { Contains, Implements, Overrides, Calls }
   ```

2. **Contains Relationships:**
   - After building the full ProjectModel tree, walk it to emit explicit Contains edges:
     - Project → Namespace (for each top-level namespace)
     - Namespace → Namespace (for nested namespaces)
     - Namespace → Type (for each type in the namespace)
     - Type → Type (for nested types)
     - Type → Method (for each method in the type)
   - Each relationship uses the stable IDs from SymbolIdGenerator
   - This flattened representation enables graph queries without tree traversal

3. **Implements Relationships:**
   - For each TypeModel, create Implements edges to each interface in ImplementedInterfaces
   - Use `typeSymbol.Interfaces` for directly implemented (not inherited) interfaces
   - Generate interface IDs using the same SymbolIdGenerator for consistency
   - Handle explicit interface implementations: `typeSymbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.ExplicitInterfaceImplementations.Any())`
   - Create method-level implements relationships for explicit implementations

4. **Overrides Relationships:**
   - Check `methodSymbol.OverriddenMethod` for override relationships
   - Create Overrides edge from overriding method to base method
   - Walk the override chain to find the original virtual declaration

5. **Extraction Output:**
   - Create `ExtractionResult` record containing:
     - `ProjectModel Model` - the hierarchical tree
     - `List<Relationship> Relationships` - the flattened graph edges
   - Update the main extraction method to return ExtractionResult

6. **Multi-Project Support:**
   - `ExtractSolution(IEnumerable<(Compilation, string name, string path)> projects) -> List<ExtractionResult>`
   - Each project produces its own ExtractionResult
   - Cross-project relationships (implementing interface from another project) should reference the same stable IDs
