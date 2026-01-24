Show afferent/efferent coupling and instability metrics: $ARGUMENTS

Steps:
1. Run `ai-code-graph coupling --level namespace --top 20 --db ./ai-code-graph/graph.db` (use "type" level if $ARGUMENTS contains "type")
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results showing Ca (afferent), Ce (efferent), I (instability), A (abstractness), D (distance from main sequence)
4. Highlight components with high instability (I > 0.8) as fragile - lots of outgoing dependencies
5. Highlight components with high distance (D > 0.5) as violating the main sequence principle
6. Suggest which namespaces/types might benefit from refactoring to reduce coupling
