Check for forbidden dependencies: $ARGUMENTS

Steps:
1. Run `ai-code-graph check-deps --db ./ai-code-graph/graph.db` (use $ARGUMENTS for custom rules if provided)
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present any violations of dependency rules:
   - Layer violations (e.g., Domain -> Infrastructure)
   - Circular dependencies
   - Forbidden namespace dependencies
4. For each violation, show the dependency chain and suggest how to fix it
5. If no violations found, confirm the architecture is clean
