Explore method call graph: $ARGUMENTS

Steps:
1. Run `ai-code-graph callgraph --method "$ARGUMENTS" --depth 2 --direction both --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the call tree showing which methods call this method (callers) and which methods it calls (callees)
4. Highlight any deep call chains or circular dependencies
5. If modifying this method, note which callers might be affected
