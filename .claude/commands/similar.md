Find methods similar to: $ARGUMENTS

Steps:
1. Run `ai-code-graph similar "$ARGUMENTS" --top 10 --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the ranked list of similar methods with their similarity scores
4. For high-similarity matches (>0.8), suggest whether they might be duplicates worth consolidating
5. For moderate matches (0.5-0.8), note they share similar intent but different implementations
