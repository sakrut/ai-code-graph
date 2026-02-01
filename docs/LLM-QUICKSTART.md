# AI Code Graph — LLM Quickstart (minimal context, minimal tokens)

## What you get
A precomputed, semantically-correct view of a .NET solution:
- call graph (incl. interface dispatch / overrides where possible)
- cognitive complexity hotspots
- dead-code candidates
- coupling/instability metrics (if enabled)

Goal: let an LLM/agent answer “what should I look at?” in **1 call**, not 10.

## 1) Build the graph (one-time per repo state)
```bash
ai-code-graph analyze path/to/YourSolution.sln
# output: ./ai-code-graph/graph.db
```

Tip: run this after major changes or in CI.

## 2) Before editing a method: get compact context
```bash
ai-code-graph context "Namespace.Type.Method" --db ./ai-code-graph/graph.db
```
Use this as the default pre-edit ritual.

What you want to see:
- CC/LOC/Nesting
- direct callers + direct callees
- duplicates / cluster membership (if enabled)

## 3) If change may have blast radius: impact + callgraph
```bash
ai-code-graph impact "Namespace.Type.Method" --depth 3
ai-code-graph callgraph "Namespace.Type.Method" --direction both --depth 2
```

## 4) If refactoring: find the highest-leverage places
```bash
ai-code-graph hotspots --top 20 --threshold 10
ai-code-graph dead-code
ai-code-graph duplicates --threshold 0.85
```

## 5) If results look stale
Re-run analyze:
```bash
ai-code-graph analyze path/to/YourSolution.sln
```

## Recommended defaults (token economy)
For agent integrations, prefer:
- bounded outputs (`--top`, `--threshold`, `--depth`)
- compact formatting (one item per line)
- stable method identifiers when available
