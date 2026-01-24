Analyze solution and build code graph.

Steps:
1. Look for a .sln file in the current directory or use the path provided: $ARGUMENTS
2. Run `ai-code-graph analyze "$ARGUMENTS" --save-baseline` (or without path if auto-discovering)
3. Wait for analysis to complete and report the summary stats
4. Confirm the database was created at ./ai-code-graph/graph.db
5. Inform the user that /context, /hotspots, /duplicates, /drift, and other commands are now available
