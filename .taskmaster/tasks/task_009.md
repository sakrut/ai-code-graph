# Task ID: 9

**Title:** Implement Intent Normalization Module

**Status:** done

**Dependencies:** 3 ✓, 4 ✓

**Priority:** medium

**Description:** For each method, generate a normalized structural signature and semantic payload text by tokenizing identifiers, normalizing AST structure, and producing intent-descriptive text for embedding.

**Details:**

1. Create `IntentNormalizer` class:
   ```csharp
   public record NormalizedMethod(
       string MethodId,
       string StructuralSignature,  // Normalized AST shape
       string SemanticPayload        // Human-readable intent text
   );
   
   public class IntentNormalizer
   {
       public NormalizedMethod Normalize(MethodModel method, SyntaxNode methodSyntax)
       {
           var structural = BuildStructuralSignature(methodSyntax);
           var semantic = BuildSemanticPayload(method, methodSyntax);
           return new NormalizedMethod(method.Id, structural, semantic);
       }
   }
   ```
2. **Structural Signature** generation:
   - Replace all literals with placeholder tokens (`LIT_STR`, `LIT_NUM`)
   - Replace local variable names with positional names (`v0`, `v1`)
   - Keep control flow structure (if/for/while/switch)
   - Keep method call names but normalize receiver
   - Result is a canonical AST shape string
3. **Semantic Payload** generation:
   - Split PascalCase/camelCase identifiers: `RemoveCustomerTag` → `remove customer tag`
   - Include: method name tokens, parameter type names, return type
   - Include: called method name tokens
   - Include: string literals (potential intent signals)
   - Concatenate into natural language description
4. **Identifier tokenization:**
   - PascalCase split: `GetCustomerById` → [`Get`, `Customer`, `By`, `Id`]
   - Acronym handling: `HTTPClient` → [`HTTP`, `Client`]
   - Lowercase normalization
5. Store normalized data in SQLite (add columns to Methods table or separate NormalizedMethods table)

**Test Strategy:**

Test structural signature: two methods with same logic but different variable names should produce identical signatures. Test semantic payload: verify PascalCase splitting, acronym handling, and payload includes relevant tokens. Test with various naming conventions. Verify normalization is deterministic.

## Subtasks

### 9.1. Implement Identifier Tokenizer with PascalCase/camelCase Splitting and Acronym Handling

**Status:** done  
**Dependencies:** None  

Create a utility class that splits compound identifiers into their constituent tokens, handling PascalCase, camelCase, acronyms (e.g., HTTPClient → [HTTP, Client]), underscores, and edge cases like consecutive uppercase letters.

**Details:**

Create an `IdentifierTokenizer` static class with a `Tokenize(string identifier)` method that returns `IReadOnlyList<string>`. Implement splitting logic: 1) Split on underscores first, 2) For each segment, detect transitions between lowercase→uppercase (camelCase boundary), uppercase→uppercase+lowercase (acronym boundary like 'HTTPClient' → 'HTTP'+'Client'), 3) Handle edge cases: single-letter words, all-caps identifiers, numeric segments ('Get2ndItem' → ['Get', '2nd', 'Item']), trailing acronyms ('getURL' → ['get', 'URL']). Add a `TokenizeAndNormalize(string identifier)` method that returns lowercase tokens. Include comprehensive unit tests covering: standard PascalCase ('GetCustomerById' → ['Get','Customer','By','Id']), acronyms ('XMLHTTPRequest' → ['XML','HTTP','Request']), camelCase ('removeTag' → ['remove','Tag']), mixed ('getURLForID' → ['get','URL','For','ID']), underscored ('get_customer_id' → ['get','customer','id']).

### 9.2. Implement Structural Signature Generation from AST

**Status:** done  
**Dependencies:** None  

Build the structural signature generator that walks a Roslyn SyntaxNode tree and produces a canonical, normalized string representation preserving control flow shape while replacing literals and local variable names with positional placeholders.

**Details:**

Create a `StructuralSignatureBuilder` class with a `Build(SyntaxNode methodSyntax)` method returning a canonical string. Implement a CSharpSyntaxWalker or recursive visitor that: 1) Replaces all string literals with 'LIT_STR', numeric literals with 'LIT_NUM', boolean literals with 'LIT_BOOL', null with 'LIT_NULL', 2) Replaces local variable declarations and references with positional names (v0, v1, v2) based on declaration order, 3) Preserves control flow keywords and structure (if/else/for/foreach/while/do/switch/case/try/catch/finally), 4) Preserves method call names but normalizes the receiver to 'recv' (e.g., 'this.Foo()' and 'obj.Foo()' both become 'recv.Foo()'), 5) Preserves operators and expression structure, 6) Outputs a deterministic, whitespace-normalized string. Track variable name mappings in a dictionary during traversal to ensure consistent positional naming.

### 9.3. Implement Semantic Payload Generation

**Status:** done  
**Dependencies:** 9.1  

Build the semantic payload generator that combines tokenized identifiers from method names, parameter types, return types, called method names, and string literals into a natural language description suitable for embedding.

**Details:**

Create a `SemanticPayloadBuilder` class with a `Build(MethodModel method, SyntaxNode methodSyntax)` method returning a concatenated natural language string. Implementation: 1) Tokenize and lowercase the method name using IdentifierTokenizer ('RemoveCustomerTag' → 'remove customer tag'), 2) Tokenize parameter type names and include them ('List<Customer>' → 'list customer'), 3) Include the return type tokenized, 4) Walk the syntax tree to find all InvocationExpressions and tokenize called method names, 5) Extract string literal values from the method body as potential intent signals, 6) Concatenate all tokens into a space-separated natural language string with section markers or ordering: '[method] remove customer tag [params] string customer id [returns] bool [calls] find customer delete tag [literals] customer not found'. Ensure deduplication of repeated tokens and consistent ordering for determinism.

### 9.4. Create IntentNormalizer Class Combining Structural and Semantic Normalization

**Status:** done  
**Dependencies:** 9.2, 9.3  

Implement the main IntentNormalizer class that orchestrates structural signature and semantic payload generation for each method, producing a NormalizedMethod record with both representations.

**Details:**

Create the `NormalizedMethod` record type with properties: MethodId (string), StructuralSignature (string), SemanticPayload (string). Create the `IntentNormalizer` class that: 1) Takes dependencies on StructuralSignatureBuilder and SemanticPayloadBuilder (inject via constructor for testability), 2) Implements `NormalizedMethod Normalize(MethodModel method, SyntaxNode methodSyntax)` that calls both builders and returns the combined result, 3) Implements `IReadOnlyList<NormalizedMethod> NormalizeAll(IEnumerable<(MethodModel, SyntaxNode)> methods)` for batch processing, 4) Handles error cases gracefully (null syntax nodes, methods without bodies like abstract/interface methods - produce empty structural signature but still generate semantic payload from declaration), 5) Add logging for normalization statistics (methods processed, failures). Ensure the class is registered in the DI container if the project uses one.

### 9.5. Add NormalizedMethods Storage Table in SQLite

**Status:** done  
**Dependencies:** 9.4  

Create a NormalizedMethods table in the SQLite database to persist structural signatures and semantic payloads, with methods to store and retrieve normalized data linked to method IDs.

**Details:**

Add a `NormalizedMethods` table to the SQLite schema with columns: MethodId (TEXT PRIMARY KEY, FK to Methods), StructuralSignature (TEXT NOT NULL), SemanticPayload (TEXT NOT NULL), NormalizedAt (TEXT, ISO 8601 timestamp). Create or extend a repository class with methods: 1) `SaveNormalizedMethod(NormalizedMethod method)` - upsert a single normalized method, 2) `SaveNormalizedMethods(IEnumerable<NormalizedMethod> methods)` - batch upsert with transaction for performance, 3) `GetNormalizedMethod(string methodId)` - retrieve by ID, 4) `GetAllNormalizedMethods()` - retrieve all for batch embedding generation, 5) `GetMethodsNeedingNormalization()` - find methods without normalization entries (for incremental processing). Use parameterized queries to prevent SQL injection. Add migration logic to create the table if it doesn't exist. Ensure the foreign key to the Methods table is properly defined.
