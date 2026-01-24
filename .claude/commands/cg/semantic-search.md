Search code by semantic meaning: $ARGUMENTS

Steps:
1. Run `ai-code-graph semantic-search "$ARGUMENTS" --top 10 --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. If a warning about hash-based embeddings appears, inform the user they can re-analyze with `--embedding-engine openai` for better results
4. Present the results ranked by similarity score
5. For the top results, briefly describe what the method does based on its name and location
6. Suggest which method(s) are most relevant to the user's query
