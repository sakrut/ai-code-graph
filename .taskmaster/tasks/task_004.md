# Task ID: 4

**Title:** Implement SQLite Storage Layer

**Status:** done

**Dependencies:** 1 ✓

**Priority:** high

**Description:** Create the SQLite database schema and data access layer for persisting the code graph, metrics, and relationships. Store only the latest snapshot (overwrite on each analysis run).

**Details:**

1. Create SQLite schema with tables:
   ```sql
   CREATE TABLE Projects (Id TEXT PRIMARY KEY, Name TEXT, FilePath TEXT);
   CREATE TABLE Namespaces (Id TEXT PRIMARY KEY, FullName TEXT, ProjectId TEXT REFERENCES Projects);
   CREATE TABLE Types (Id TEXT PRIMARY KEY, Name TEXT, FullName TEXT, Kind TEXT, NamespaceId TEXT REFERENCES Namespaces);
   CREATE TABLE Methods (Id TEXT PRIMARY KEY, Name TEXT, FullName TEXT, ReturnType TEXT, TypeId TEXT REFERENCES Types, StartLine INT, EndLine INT, FilePath TEXT);
   CREATE TABLE MethodCalls (CallerId TEXT REFERENCES Methods, CalleeId TEXT REFERENCES Methods, PRIMARY KEY(CallerId, CalleeId));
   CREATE TABLE TypeImplements (TypeId TEXT REFERENCES Types, InterfaceId TEXT REFERENCES Types, PRIMARY KEY(TypeId, InterfaceId));
   CREATE TABLE Metrics (MethodId TEXT PRIMARY KEY REFERENCES Methods, CognitiveComplexity INT, LinesOfCode INT, NestingDepth INT);
   CREATE TABLE IntentClusters (Id TEXT PRIMARY KEY, Label TEXT, Description TEXT);
   CREATE TABLE MethodClusterMap (MethodId TEXT REFERENCES Methods, ClusterId TEXT REFERENCES IntentClusters, Score REAL, PRIMARY KEY(MethodId, ClusterId));
   ```
2. Create `StorageService` class with methods:
   - `InitializeAsync()` - create/recreate database
   - `SaveCodeModelAsync(List<ProjectModel>)` - bulk insert structural data
   - `SaveCallGraphAsync(List<MethodCall>)` - insert call edges
   - `SaveMetricsAsync(List<MethodMetrics>)` - insert metrics
   - Query methods for CLI: `GetMethodById`, `GetCallees`, `GetCallers`, `GetHotspots`, etc.
3. Database path: `./ai-code-graph/graph.db`
4. Use transactions for bulk inserts
5. Drop and recreate tables on each full analysis (latest snapshot only)
6. Add indexes on frequently queried columns (FullName, CognitiveComplexity)

**Test Strategy:**

Unit test all CRUD operations with in-memory SQLite. Test schema creation, bulk inserts, query methods. Verify foreign key constraints. Test that re-analysis overwrites previous data correctly. Benchmark bulk insert performance with 5000+ methods.

## Subtasks

### 4.1. Create SQLite Database Schema with Tables and Indexes

**Status:** done  
**Dependencies:** None  

Define the complete SQLite schema including all 9 tables (Projects, Namespaces, Types, Methods, MethodCalls, TypeImplements, Metrics, IntentClusters, MethodClusterMap) plus a NormalizedMethods table for future phases, with appropriate foreign key constraints and performance indexes.

**Details:**

Create a SQL schema definition (as embedded resource or constants class) containing:

1. All tables with correct column types and constraints:
   - Projects (Id TEXT PK, Name TEXT, FilePath TEXT)
   - Namespaces (Id TEXT PK, FullName TEXT, ProjectId TEXT FK)
   - Types (Id TEXT PK, Name TEXT, FullName TEXT, Kind TEXT, NamespaceId TEXT FK)
   - Methods (Id TEXT PK, Name TEXT, FullName TEXT, ReturnType TEXT, TypeId TEXT FK, StartLine INT, EndLine INT, FilePath TEXT)
   - MethodCalls (CallerId TEXT FK, CalleeId TEXT FK, composite PK)
   - TypeImplements (TypeId TEXT FK, InterfaceId TEXT FK, composite PK)
   - Metrics (MethodId TEXT PK FK, CognitiveComplexity INT, LinesOfCode INT, NestingDepth INT)
   - IntentClusters (Id TEXT PK, Label TEXT, Description TEXT)
   - MethodClusterMap (MethodId TEXT FK, ClusterId TEXT FK, Score REAL, composite PK)
   - NormalizedMethods (MethodId TEXT PK FK, NormalizedSource TEXT, TokenHash TEXT) for future duplicate detection

2. Indexes for query performance:
   - IX_Methods_FullName on Methods(FullName)
   - IX_Types_FullName on Types(FullName)
   - IX_Metrics_CognitiveComplexity on Metrics(CognitiveComplexity DESC)
   - IX_MethodCalls_CalleeId on MethodCalls(CalleeId) for reverse lookups
   - IX_Namespaces_ProjectId on Namespaces(ProjectId)
   - IX_Types_NamespaceId on Types(NamespaceId)
   - IX_Methods_TypeId on Methods(TypeId)

3. Use PRAGMA foreign_keys = ON for constraint enforcement.
4. Define table creation order respecting FK dependencies: Projects → Namespaces → Types → Methods → MethodCalls/TypeImplements/Metrics → IntentClusters → MethodClusterMap.

### 4.2. Implement StorageService Class with InitializeAsync

**Status:** done  
**Dependencies:** 4.1  

Create the StorageService class with database lifecycle management including InitializeAsync that creates/recreates the database, manages connection strings, and handles the drop-and-recreate strategy for snapshot-based storage.

**Details:**

Create `StorageService` class using Microsoft.Data.Sqlite:

1. Constructor accepts optional database path (default: `./ai-code-graph/graph.db`)
2. Manage SqliteConnection lifecycle (implement IDisposable/IAsyncDisposable)
3. `InitializeAsync()` method:
   - Ensure directory exists for database file
   - Drop all tables in reverse FK-dependency order (MethodClusterMap → IntentClusters → Metrics → TypeImplements → MethodCalls → Methods → Types → Namespaces → Projects → NormalizedMethods)
   - Create all tables and indexes using schema from subtask 1
   - Enable WAL mode for better concurrent read performance: PRAGMA journal_mode=WAL
   - Enable foreign keys: PRAGMA foreign_keys = ON
4. Add `GetConnectionString()` helper that builds connection string with appropriate settings
5. Add internal `GetConnectionAsync()` method for use by other StorageService methods
6. Use `Microsoft.Data.Sqlite` NuGet package with parameterized queries throughout
7. Ensure thread-safety considerations for the connection (single writer, multiple readers with WAL)

### 4.3. Implement Bulk Insert Methods with Transaction Batching

**Status:** done  
**Dependencies:** 4.2  

Implement SaveCodeModelAsync, SaveCallGraphAsync, and SaveMetricsAsync methods that efficiently bulk-insert structural data, call graph edges, and metrics using parameterized queries within transactions.

**Details:**

Implement three bulk insert methods on StorageService:

1. `SaveCodeModelAsync(List<ProjectModel> projects)`:
   - Wrap entire operation in a transaction for atomicity and performance
   - Insert Projects, then Namespaces, then Types (with Kind), then Methods in FK order
   - Insert TypeImplements relationships from TypeModel.ImplementedInterfaces
   - Use parameterized INSERT statements with command reuse (create command once, rebind parameters)
   - Batch in groups of 500 for memory efficiency on large solutions
   - Handle the hierarchical model traversal: Project → Namespaces → Types → Methods

2. `SaveCallGraphAsync(List<MethodCall> calls)`:
   - Wrap in transaction
   - Use INSERT OR IGNORE to handle duplicate edges gracefully
   - Parameterized insert for (CallerId, CalleeId) pairs

3. `SaveMetricsAsync(List<MethodMetrics> metrics)`:
   - Wrap in transaction
   - INSERT OR REPLACE to allow metric updates
   - Insert (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth)

4. Performance considerations:
   - Reuse SqliteCommand objects with parameter rebinding
   - Use BEGIN/COMMIT transaction wrapping (massive perf improvement for SQLite)
   - Target: 5000+ methods inserted in under 2 seconds

### 4.4. Implement Query Methods for CLI Consumption

**Status:** done  
**Dependencies:** 4.3  

Implement read query methods that the CLI commands will use: GetMethodById, GetCallees, GetCallers, GetHotspots, and pattern-matching search methods with sorting and filtering capabilities.

**Details:**

Implement query methods on StorageService:

1. `GetMethodByIdAsync(string methodId)` → MethodModel?
   - Join with Types, Namespaces for full context
   - Return null if not found

2. `GetCalleesAsync(string methodId)` → List<MethodModel>
   - Query MethodCalls WHERE CallerId = @id, JOIN Methods for callee details
   - Include callee's type and namespace info

3. `GetCallersAsync(string methodId)` → List<MethodModel>
   - Query MethodCalls WHERE CalleeId = @id, JOIN Methods for caller details

4. `GetHotspotsAsync(int top = 20, string? sortBy = "complexity")` → List<(MethodModel, MethodMetrics)>
   - Join Methods with Metrics
   - ORDER BY CognitiveComplexity DESC (or LinesOfCode, NestingDepth based on sortBy)
   - LIMIT @top

5. `SearchMethodsAsync(string pattern)` → List<MethodModel>
   - Use LIKE '%pattern%' on Methods.FullName for pattern matching
   - Support wildcard patterns
   - Return with type/namespace context

6. `GetMethodsByTypeAsync(string typeId)` → List<MethodModel>
   - All methods belonging to a type

7. `GetInterfaceImplementorsAsync(string interfaceId)` → List<TypeModel>
   - Query TypeImplements for implementing types

8. All queries use parameterized statements to prevent SQL injection.
9. Return domain model objects (not raw readers) for clean API boundaries.

### 4.5. Add NormalizedMethods Table and Vector Storage Schema

**Status:** done  
**Dependencies:** 4.2  

Extend the schema with NormalizedMethods for code clone detection and add vector/embedding storage tables for the semantic search phase, preparing the database for future analysis capabilities.

**Details:**

Extend StorageService with additional tables and methods for later phases:

1. **NormalizedMethods table** (for duplicate/clone detection - Task 11):
   - Schema: MethodId TEXT PK FK→Methods, NormalizedSource TEXT, TokenHash TEXT
   - Index on TokenHash for fast structural clone lookup
   - `SaveNormalizedMethodsAsync(List<NormalizedMethod>)` - bulk insert with transaction
   - `GetMethodsByTokenHashAsync(string hash)` → List<MethodModel> for finding structural clones

2. **Embeddings storage** (for semantic search - Task 10):
   - Schema: CREATE TABLE Embeddings (MethodId TEXT PK FK→Methods, Vector BLOB, ModelVersion TEXT)
   - Store float[] as BLOB (serialize/deserialize with BinaryWriter/Reader or BitConverter)
   - `SaveEmbeddingsAsync(List<(string MethodId, float[] Vector, string ModelVersion)>)` - bulk insert
   - `GetAllEmbeddingsAsync()` → List<(string MethodId, float[] Vector)> for building in-memory index
   - `GetEmbeddingAsync(string methodId)` → float[]?

3. **IntentClusters and MethodClusterMap** save/query methods:
   - `SaveClustersAsync(List<IntentCluster>)` - save cluster definitions
   - `SaveMethodClusterMappingsAsync(List<MethodClusterMapping>)` - save method-to-cluster assignments
   - `GetMethodsByClusterAsync(string clusterId)` → List<(MethodModel, float Score)>
   - `GetClustersAsync()` → List<IntentCluster>

4. All tables included in InitializeAsync drop/create cycle.
5. Use parameterized queries and transactions for all bulk operations.
