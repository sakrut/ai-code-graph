Show complexity hotspots in the codebase.

Steps:
1. Run `ai-code-graph hotspots --top 15 --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results, highlighting methods with CC > 15 as candidates for refactoring
4. For the top 3 hotspots, briefly suggest what makes them complex (deep nesting, many branches, etc.)
