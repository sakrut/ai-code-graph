# AI Code Graph for .NET  
## Product Design Requirements (PDR)

---

## 1. Problem Statement

Modern .NET systems (even single-repo ones) quickly grow to thousands of classes and methods.  
AI-assisted development, architectural governance, and change impact analysis require a **machine-queryable, semantic understanding of the codebase**, not just raw text.

The goal is to build a **Roslyn-based static analysis system** that runs automatically after every build/tests and produces structured artifacts that describe:

- The full code structure (namespace → type → method)
- Method-level dependency graph (call graph)
- Interface → implementation mappings
- Cognitive complexity hotspots
- Semantic intent clusters (e.g., permission checks, tag management)
- Duplicate and scattered logic detection
- Natural-language-to-code mapping (e.g., “remove customer tag”)

These artifacts are consumed by an AI agent (via CLI) to rapidly reconstruct context, perform semantic search, detect architectural drift, and guide refactoring or feature changes.

---

## 2. Goals

### Functional Goals

1. Automatically run after each `dotnet build` / `dotnet test`
2. Produce a complete **structural + semantic graph** of the codebase
3. Compute **cognitive complexity** per method
4. Build a **method-level call graph**
5. Detect:
   - Structural duplicates
   - Semantic duplicates (same intent, different implementation)
6. Support **natural language search** over code intent
7. Provide **CLI access** for:
   - Call graph exploration
   - Hotspot analysis
   - Duplicate detection
   - Intent-based search
8. Provide artifacts to serve as **AI context substrate** for future sessions

### Non-Goals (v1)

- Runtime tracing or profiling
- Multi-repo aggregation
- Cloud LLM dependency (local-only, OSS)
- Long-term historical versioning (latest snapshot only)

---

## 3. Scope

| Dimension | Decision |
|------------|----------|
| Language | C# (.NET only) |
| Repo Type | Single-repo |
| Execution | Local build and CI |
| Storage | Latest snapshot only |
| Fields | Not modeled |
| Services / HTTP edges | Not modeled in v1 |
| Metrics | Cognitive complexity (primary) |
| Similarity | Hybrid: AST + embeddings |
| Licensing | Fully open source |
| Output Path | `./ai-code-graph/` |

---

## 4. System Architecture (High-Level)

```

dotnet build / test
|
v
+---------------------+
| ai-code-graph CLI  |
+---------------------+
|
v
+---------------------------+
| Roslyn Workspace Loader  |
+---------------------------+
|
v
+---------------------------+
| Code Model Extractor     |
| - Projects               |
| - Namespaces             |
| - Types                  |
| - Methods                |
+---------------------------+
|
v
+---------------------------+
| Dependency Graph Builder |
| - Call Graph             |
| - Interface Mapping      |
+---------------------------+
|
v
+---------------------------+
| Metrics Engine           |
| - Cognitive Complexity   |
+---------------------------+
|
v
+---------------------------+
| Intent Normalization     |
| - AST shape              |
| - Identifier tokens     |
+---------------------------+
|
v
+---------------------------+
| Embedding Engine (Local) |
+---------------------------+
|
v
+---------------------------+
| Similarity & Clustering |
+---------------------------+
|
v
+---------------------------+
| Storage Layer            |
| - SQLite (graph, metrics)|
| - Vector Index           |
+---------------------------+
|
v
+---------------------------+
| CLI Query Interface      |
| - Search                 |
| - Duplicates             |
| - Call Graph             |
| - Drift                  |
+---------------------------+

```

---

## 5. High-Level Components

### 5.1 Build Integration Layer
- .NET Global Tool: `ai-code-graph`
- Invoked as:
```

dotnet build
ai-code-graph analyze

````
- Outputs to: `./ai-code-graph/`

---

### 5.2 Roslyn Workspace Loader
Responsibilities:
- Load `.sln` using `MSBuildWorkspace`
- Build semantic model
- Produce stable symbol IDs

---

### 5.3 Code Model Extractor

Extract:
- Project
- Namespace
- Type (class, interface, record)
- Method

Relations:
- Contains
- Implements

---

### 5.4 Call Graph Builder

Edges:
- Method → Method (invocation)
- Interface → Implementing Method

---

### 5.5 Metrics Engine

Compute per method:
- Cognitive Complexity (primary)
- Lines of Code
- Nesting Depth

---

### 5.6 Intent Normalization Module

For each method:
- Tokenize identifiers (PascalCase split)
- Normalize AST (remove literals, rename locals)
- Generate:
- Structural signature
- Semantic payload text

---

### 5.7 Embedding & Vector Index

- Local open-source embedding model
- Vectors per method
- kNN search
- Stored in local vector index (sidecar to SQLite)

---

### 5.8 Duplicate & Intent Clustering

Detect:
- Structural clones (AST similarity)
- Semantic clones (embedding similarity)
- Hybrid score

Produce:
- Intent clusters (e.g., “permission check”, “customer tag removal”)

---

### 5.9 Storage Layer

#### SQLite Schema (Core)

Tables:
- Projects
- Namespaces
- Types
- Methods
- MethodCalls
- TypeImplements
- Metrics
- IntentClusters
- MethodClusterMap

Vector Index:
- Stored in `./ai-code-graph/vectors/`

---

### 5.10 Diff & Drift Engine

Compare:
- Latest vs previous build (workspace cache)
- Or vs `main` artifact

Detect:
- New semantic duplicates
- Complexity regressions
- New scattering of intent clusters

---

### 5.11 CLI Interface (`ai-code-graph`)

Examples:

```bash
ai-code-graph analyze
ai-code-graph search "remove customer tag"
ai-code-graph duplicates --concept permission
ai-code-graph callgraph RemoveCustomerTagHandler --depth 3
ai-code-graph hotspots --complexity
ai-code-graph drift --vs main
ai-code-graph export --concept "CustomerTag" --format json
````

---

## 6. AI Agent Integration Contract

The AI agent interacts only via CLI:

Capabilities:

* Query graph slices
* Fetch semantic clusters
* Fetch call graph subtrees
* Retrieve complexity hotspots
* Retrieve duplicate implementations

All results returned as:

* JSON
* Deterministic, tool-friendly

---

## 7. Functional Requirements

| ID   | Requirement                             |
| ---- | --------------------------------------- |
| FR1  | Extract full namespace/type/method tree |
| FR2  | Build method-level call graph           |
| FR3  | Compute cognitive complexity            |
| FR4  | Compute semantic embeddings locally     |
| FR5  | Detect semantic duplicates              |
| FR6  | Cluster methods by intent               |
| FR7  | Support NL → code search                |
| FR8  | Provide CLI query interface             |
| FR9  | Compare build vs previous/main          |
| FR10 | Store latest snapshot only              |

---

## 8. Non-Functional Requirements

* Execution time: ≤ 2 minutes on typical repo
* Offline (no cloud calls)
* Fully open-source stack
* Deterministic output
* Reproducible builds

---

## 9. Roadmap

### Phase 1 – Structural Intelligence (v1)

* Roslyn model
* Call graph
* Cognitive complexity
* SQLite storage
* CLI: tree, callgraph, hotspots

### Phase 2 – Semantic Intelligence (v2)

* Embeddings
* Intent clustering
* Semantic duplicate detection
* NL search

### Phase 3 – Architectural Governance (v3)

* Drift detection rules
* Scattered responsibility detection
* Policy checks (e.g., “permission logic must live in PermissionService”)
* AI-guided refactoring suggestions

---

## 10. Vision

`ai-code-graph` becomes the **semantic nervous system of the codebase**:

* The structural brain for AI agents
* The architectural memory for humans
* The intent map that prevents logic scattering
* The foundation for true AI-assisted system evolution

```
