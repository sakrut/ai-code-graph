# ai-code-graph — Product Direction & Technical Roadmap (GPT PDR)

> Source: user-provided PDR. Assumption: this document is correct and should drive planning.

## 1) What This Repository IS (and IS NOT)

### IS: Semantic Code Intelligence Engine for AI Agents in Legacy .NET
- Roslyn-based semantic graph as the source of truth
- Precomputed, deterministic analysis
- AI agents consume facts, never infer architecture
- CLI / MCP-first integration (Claude Code, Codex, Continue)

### IS NOT
- Not a coding agent
- Not an IDE replacement
- Not a generic RAG framework
- Not a vector-search-first system

## 2) Core Principles (Non-Negotiable)
1. Roslyn > LLM inference
2. Graph-first, AI-second
3. Precompute what is expensive
4. .NET-first focus (avoid multi-language dilution)

## 3) Current Strengths (Keep & Double Down)
- Roslyn semantic graph (accurate symbol resolution, call graphs, dependencies, generics, DI)
- Precomputed graph as a knowledge base (fast, deterministic, stable across sessions)
- MCP / tool interface (`cg:*`) for infra-level integration

## 4) Key Problems to Fix

### 4.1 Token search as primary retrieval
Problem: shallow relevance, no structural understanding.
Direction: replace with graph-first retrieval: graph traversal → ranking → optional vector recall.

### 4.2 No formal query model
Problem: many commands, no unified query abstraction.
Direction: introduce a Graph Query Schema (seed/expand/depth/filters/rank). Benefits: easier for AI, cacheable, testable.

### 4.3 Missing architectural facts
Problem: architecture is implicit.
Direction: precompute architectural primitives:
- layer detection (API/Application/Domain/Infra)
- hotspots (churn + complexity)
- blast radius
- forbidden dependencies
- “do not touch” zones

## 5) What to explicitly avoid
- Generic vector RAG as the primary approach
- Competing with agents/IDEs via UX/codegen

## 6) Strategic positioning
ai-code-graph = Semantic Code Intelligence Layer for AI agents working in legacy .NET.
Target users: senior devs, tech leads, architects, AI-assisted teams onboarding legacy systems.

## 7) Recommended technical roadmap

### Sprint 1 — Graph-native retrieval
- graph traversal engine
- ranking strategies: blast radius, complexity, coupling
- replace token search as default

### Sprint 2 — Query & architecture layer
- unified query schema
- architectural facts extraction
- layer detection
- dependency violation detection

### Sprint 3 — Hybrid retrieval (optional)
- embeddings per graph node
- vector search only for recall
- graph always decides relevance

### Sprint 4 — Memory integration
- integrate with Zep / Mem0
- store decisions, historical reasons, danger zones

## 8) Ideal AI workflow
1) AI asks high-level question
2) ai-code-graph returns subgraph + architectural facts + ranked nodes
3) AI reasons on stable context
4) coding agent executes changes

## 9) Success criteria
- fewer tokens required
- fewer exploratory calls
- stable understanding across sessions
- safer refactors
- faster onboarding
