Show detected code duplicates in the codebase.

Steps:
1. Run `ai-code-graph duplicates --top 15 --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Group the results by clone type (Type1 = exact, Type2 = renamed, Semantic = similar logic)
4. For Type1 clones, suggest extracting a shared utility method
5. For Semantic clones, suggest whether they represent a pattern worth abstracting
