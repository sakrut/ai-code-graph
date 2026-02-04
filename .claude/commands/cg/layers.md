Show architectural layer assignments: $ARGUMENTS

Steps:
1. Run `ai-code-graph layers --db ./ai-code-graph/graph.db` (filter by $ARGUMENTS if provided)
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the layer assignments showing which namespaces/types belong to which architectural layers:
   - Presentation (Controllers, Views, Pages)
   - Application (Services, Handlers, UseCases)
   - Domain (Entities, ValueObjects, Aggregates)
   - Infrastructure (Repositories, DbContexts, External)
4. Highlight any layer violations (e.g., Domain depending on Infrastructure)
