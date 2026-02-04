Search code by token overlap: $ARGUMENTS

Note: For most use cases, use `/cg:query` instead for graph-based retrieval (faster, deterministic).
Use token-search as a fallback when query returns no results or when you need fuzzy text matching.

Steps:
1. Run `ai-code-graph token-search "$ARGUMENTS" --top 10 --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results ranked by similarity score
4. For the top results, briefly describe what the method does based on its name and location
5. Suggest which method(s) are most relevant to the user's query
