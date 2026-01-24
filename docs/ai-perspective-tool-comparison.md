# CG Tools vs AI Agent Native Capabilities: An Honest Comparison

> Written from the perspective of an AI coding agent (Claude) that has used both approaches on a real .NET codebase. This is not marketing — it's an empirical assessment of where pre-computed code graph tools outperform, match, or underperform an AI agent's built-in exploration workflow.

## Context

An AI coding agent like Claude Code has access to:
- **Explore agent** — multi-step autonomous search (Glob → Grep → Read in loops)
- **Grep** — regex content search across files
- **Glob** — file pattern matching
- **Read** — direct file reading
- **Reasoning** — ability to synthesize, iterate, and adapt search strategies

The question: **Do pre-computed code graph tools (Roslyn-based static analysis stored in SQLite) provide value beyond what an AI agent can already do?**

---

## Executive Summary

| Category | Tools | Verdict |
|----------|-------|---------|
| Irreplaceable | `coupling`, `hotspots`, `dead-code` | Compute metrics impossible for an AI to derive from text alone |
| Faster | `context`, `tree`, `impact` | Same info the AI could gather, but in 1 call instead of 5-10 |
| Comparable | `clusters`, `similar` | Provide algorithmic groupings the AI can partially replicate |
| Inferior | `token-search`, `semantic-search` (hash-only) | AI's Grep + reasoning produces better results |

---

## Detailed Analysis

### 1. `cg:coupling` — IRREPLACEABLE

**What it provides:** Afferent coupling (Ca), efferent coupling (Ce), instability (I), abstractness (A), and distance from the main sequence (D) for every namespace/type.

**What the AI can do instead:** Read import statements, count dependencies manually. But:
- Cannot compute instability ratios across the entire codebase in one pass
- Cannot determine abstractness (interface-to-concrete ratio) without reading every type
- Cannot calculate distance from main sequence at all

**Real output example:**
```
AiCodeGraph.Core.Storage     Ca=14, Ce=0,  I=0.00  ← Very stable, many dependents
AiCodeGraph.Core.Embeddings  Ca=10, Ce=0,  I=0.00  ← Also stable
AiCodeGraph.Tests            Ca=0,  Ce=15, I=1.00  ← Fully unstable (normal for tests)
```

**Why it matters for AI-assisted development:** When the AI needs to decide where to place new code, coupling metrics answer "which modules are stable (don't touch) vs. volatile (safe to modify)" objectively. Without this, the AI relies on heuristics like "this folder has more files so it's probably important."

**Verdict:** No AI workflow can replicate this. The computation requires full Roslyn semantic analysis.

---

### 2. `cg:hotspots` — IRREPLACEABLE

**What it provides:** Cognitive complexity (CC), lines of code (LOC), and max nesting depth for every method, ranked.

**What the AI can do instead:** Read a method and estimate "this looks complex." But:
- Cannot compute cognitive complexity scores (requires AST walking with specific increment rules)
- Cannot rank all methods across the codebase — would need to read every file
- Even if it read every file, the estimate would be subjective

**Real output example:**
```
McpServer.HandleToolCall    CC=59  LOC=120  Nest=6  ← Extreme complexity
IntentClusterer.Dbscan      CC=23  LOC=37   Nest=5  ← High complexity in compact code
MetricsEngine.ComputeMetrics CC=18 LOC=38   Nest=3  ← Moderate
```

**Why it matters:** An AI planning a refactor can immediately identify the top candidates without reading 50 files. The CC score is an objective, repeatable metric — not "I think this is complex."

**Verdict:** Irreplaceable. Cognitive complexity computation requires Roslyn AST analysis that cannot be approximated from text.

---

### 3. `cg:dead-code` — IRREPLACEABLE

**What it provides:** All methods with zero callers in the call graph.

**What the AI can do instead:** Grep for a specific method name to check if it's called. But:
- Grep is reactive (must know what to search for), dead-code is proactive
- Grep finds text matches, not semantic calls (matches in comments, strings, similar names)
- Grep cannot handle interface dispatch (`IFoo.Bar()` calling `FooImpl.Bar()`)
- To check the entire codebase, the AI would need to grep every method individually

**Real output:** Found 49 potentially unreachable methods in one call.

**Why it matters:** During refactoring, dead code identification prevents wasted effort on unused code paths. The AI would otherwise maintain and modify dead code unknowingly.

**Verdict:** Irreplaceable. Proactive identification of unreachable code requires a complete call graph with semantic resolution.

---

### 4. `cg:context` — FASTER (5x fewer tool calls)

**What it provides:** In one call: method complexity, direct callers, direct callees, cluster membership, and known duplicates.

**What the AI can do instead:** The same information via:
1. `Read` the file containing the method
2. `Grep` for callers (by method name)
3. `Read` the method body to identify callees
4. Cross-reference with other methods for duplicates

That's 4-6 tool calls producing ~200-500 lines of context. `cg:context` produces the same in 1 call with ~20 lines.

**Why it matters:** Before editing any method, the AI needs to understand its context. `cg:context` is the optimal pre-edit ritual — minimal context window usage, maximum information density.

**Limitation:** Only provides summaries. If the AI needs to read the actual implementation of a caller, it still needs `Read`.

**Verdict:** Same information, dramatically fewer round-trips. Most valuable in large codebases where each Grep returns dozens of results.

---

### 5. `cg:tree` — FASTER (for initial orientation)

**What it provides:** Complete project → namespace → type → method hierarchy with signatures.

**What the AI can do instead:**
1. `Glob **/*.cs` to find all files
2. `Read` key files to understand structure
3. Mentally construct the hierarchy

In a 50-file project, this takes 5-10 tool calls. In a 500-file project, it's impractical.

**Comparative speed:**
- `cg:tree`: 1 call, complete result
- Explore agent: 5-15 calls, partial result (only files read)

**Limitation:** Shows the snapshot from last `analyze`. If code changed since, the tree is stale. The AI's Explore agent always sees current code.

**Verdict:** Best for first-time orientation. In actively changing code, supplement with direct reads.

---

### 6. `cg:impact` — FASTER (for transitive analysis)

**What it provides:** All methods transitively affected by changing a given method (callers of callers of callers...).

**What the AI can do instead:**
1. Grep for direct callers of method X
2. For each caller found, grep for its callers
3. Repeat until no new callers found

This is O(n) grep calls where n = depth of call chain. Each grep may return false positives (text matches that aren't actual calls).

**Additional advantage:** `cg:impact` uses Roslyn's semantic model, so it correctly resolves:
- Interface implementations (`IService.Do()` → `ServiceImpl.Do()`)
- Virtual method overrides
- Implicit conversions and operator overloads

**Limitation:** In this small codebase, impact shows only 1 affected method for most queries. The value scales with codebase size.

**Verdict:** Essential for large codebases. For small projects, Grep is sufficient.

---

### 7. `cg:clusters` — COMPARABLE (algorithmic vs intuitive)

**What it provides:** DBSCAN-based grouping of methods with similar structural signatures and semantic payloads. Includes cohesion scores.

**What the AI can do instead:** After reading multiple files, recognize patterns like "these 5 methods all follow the same template." But:
- Only for files already read (not cross-codebase)
- Cannot compute cohesion scores
- May miss non-obvious groupings

**Real output quality:** Mixed.
- Good: `dispose operations` (cohesion: 1.00) — objectively correct
- Good: `visit/statement operations` (cohesion: 0.65) — recognized the Visitor pattern
- Mediocre: `save/method operations` (cohesion: 0.57) — vague label, 26 heterogeneous members

**Why it matters:** During refactoring, clusters answer "which methods should move together?" and "is this class doing too many things?" (low cohesion clusters spanning multiple types).

**Verdict:** Provides insights the AI might miss, especially cross-class similarities. But labels need human interpretation — they're generated from method name tokens, not understanding.

---

### 8. `cg:similar` — COMPARABLE (limited by embedding quality)

**What it provides:** Methods ranked by vector similarity to a target method.

**What the AI can do instead:** Read the target method, then search for methods with similar parameter types, return types, or naming patterns.

**Real output quality:**
```
IntentClusterer.ClusterMethods → similar to:
  0.42  CouplingAnalyzer.AnalyzeAsync       ← same "process list, return results" pattern
  0.42  IntentClusterer.GenerateLabel        ← same class, related logic
  0.37  HybridScorer.Merge                   ← similar data transformation
```

These are reasonable but not revelatory. An AI reading those methods would likely notice the same similarities.

**Limitation:** With hash-based embeddings (no ML model), similarity is based on token overlap in signatures and payloads. True semantic similarity (understanding what the method does) requires LLM embeddings.

**Verdict:** Useful for discovering candidates for shared abstractions. The AI could find the same patterns with more effort, but `similar` surfaces them proactively.

---

### 9. `cg:token-search` — AI IS BETTER

**What it provides:** Ranked list of methods matching a natural-language query, using hash-embedding cosine similarity.

**What the AI can do instead:** Parse the query intent, construct appropriate regex patterns, iterate on search results.

**Empirical comparison — query: "detect duplicates clone"**

Token-search results (top 3):
```
0.44  DetectClones_EmptyInput_ReturnsEmpty()     ← test method, not the implementation
0.44  DetectClones_EmptyInput_ReturnsEmpty()     ← duplicate result from different class
0.32  DetectClones_SingleMethod_ReturnsEmpty()   ← another test
```

Grep results (`detect.*clone|clone.*detect`):
```
StructuralCloneDetector.cs:  var structuralClones = structuralDetector.DetectClones(...)
SemanticCloneDetector.cs:    var semanticClones = semanticDetector.DetectClones(...)
Program.cs:                  static async Task DetectDuplicatesStage(...)
```

Grep immediately finds the production implementations. Token-search surfaces tests first.

**Why token-search loses:** Hash-based embeddings don't understand semantics. They tokenize method names and compute bag-of-words similarity. The AI understands that "detect duplicates" means the implementation, not the tests.

**Verdict:** Inferior to AI's Grep + reasoning. The AI can adapt its search strategy based on results; token-search cannot.

---

### 10. `cg:semantic-search` — DEPENDS ON CONFIGURATION

**Without LLM embeddings (hash-only):** Equivalent to token-search. Same limitations apply.

**With LLM embeddings (OpenAI/ONNX):** Potentially superior to both grep and token-search, because it could understand "methods that persist data" → `SaveMetricsAsync`, `SaveCallGraphAsync`, even if the query words don't appear in the method name.

**Current state:** This codebase uses hash embeddings (384-dim feature hashing, no ML model). Semantic search provides no advantage over grep.

**Verdict:** Only valuable with real LLM embeddings configured. Without them, skip it.

---

## Recommendations for AI Agent Workflows

### Before editing a method:
```
cg:context <method>    →  1 call, full picture
```
Replaces: Read file + Grep callers + Grep callees + assess complexity (4-6 calls)

### Before planning a refactor:
```
cg:hotspots            →  What to refactor (ranked by complexity)
cg:coupling            →  What's safe to change (high instability = safe)
cg:dead-code           →  What to delete
cg:clusters            →  What should move together
```
The AI cannot replicate any of these with Grep/Read alone.

### Before assessing change risk:
```
cg:impact <method>     →  What breaks if this changes
```
Replaces: Recursive grep for callers (3-5 rounds, missing interface dispatch)

### For finding code (use AI's native tools instead):
```
Grep + Explore agent   →  Better than token-search
Read + reasoning       →  Better than similar (for small scope)
```

### For first-time codebase orientation:
```
cg:tree                →  Fastest structural overview
cg:hotspots            →  Where the complexity lives
cg:coupling            →  Which modules are stable vs volatile
```
This trio in 3 calls gives more architectural insight than 20 rounds of Explore agent.

---

## When to Rebuild the Graph

The graph is a snapshot. It becomes stale when:
- New methods are added (not in tree, not in dead-code analysis)
- Method signatures change (impact analysis is wrong)
- New call relationships exist (callgraph is incomplete)

Rule of thumb: rebuild after any structural change (new classes, moved methods, changed signatures). Don't rebuild for internal logic changes within existing method bodies — complexity metrics update, but the call graph doesn't change.

---

## Conclusion

Pre-computed code graph tools and AI agent capabilities are **complementary, not competing**:

- **Code graph tools excel at:** Global metrics (coupling, complexity, reachability), pre-computed relationships (call graph, clones), and architectural views (tree, clusters).
- **AI agent excels at:** Understanding intent, adaptive search, reading and reasoning about implementation details, handling novel queries.

The optimal workflow uses both: code graph tools for architectural context and objective metrics, AI agent tools for precise code understanding and implementation work. Removing either degrades the quality of AI-assisted development — the graph provides the map, the AI reads the territory.
