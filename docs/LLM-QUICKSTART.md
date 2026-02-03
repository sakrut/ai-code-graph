# AI Code Graph — LLM Quickstart

**Goal:** Answer "what should I look at?" in 1 call, not 10.

## 1) Build the graph
```bash
ai-code-graph analyze YourSolution.sln
# Output: ./ai-code-graph/graph.db
```

For faster analysis (skips clustering):
```bash
ai-code-graph analyze YourSolution.sln --stages core
```

## 2) Check if graph is current
```bash
ai-code-graph status
# Shows: analyzed time, git commit, staleness warning
```

## 3) Before editing a method: context
```bash
# Find method by name
ai-code-graph context "ValidateUser"

# Use ID for follow-up calls (faster, unambiguous)
ai-code-graph context --id "MyApp.Services.UserService.ValidateUser(String)"
```

Output gives you: complexity, callers, callees, duplicates, method ID.

## 4) Check blast radius
```bash
ai-code-graph impact --id "<method-id>" --depth 3
ai-code-graph callgraph --id "<method-id>" --direction both
```

## 5) Find refactoring targets
```bash
ai-code-graph hotspots --top 10
ai-code-graph dead-code --top 10
ai-code-graph coupling --top 10
```

## Output Format

All commands default to compact format (1 line per item). Use `--format table` for human-readable output or `--format json` for scripting.

## Quick Reference

| Command | Purpose |
|---------|---------|
| `context <method>` | Method summary before editing |
| `impact --id <id>` | Transitive callers (blast radius) |
| `callgraph --id <id>` | Direct callers/callees |
| `hotspots` | High-complexity methods |
| `dead-code` | Uncalled methods |
| `coupling` | Afferent/efferent coupling |
| `status` | DB staleness check |

See [output-contract.md](output-contract.md) for format details.
