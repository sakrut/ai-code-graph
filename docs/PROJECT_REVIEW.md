# AI Code Graph — szybki przegląd (dla review)

## Co to jest
AI Code Graph to CLI narzędzie do statycznej analizy repo .NET (Roslyn), które buduje lokalny „kodowy graf” w SQLite i udostępnia go:
- jako zestaw komend CLI do szybkiej nawigacji (call graph / hotspots / duplicates / drift),
- jako MCP server (JSON-RPC stdio) dla IDE/agentów,
- oraz jako „token-efficient context substrate” dla LLM (zamiast ręcznych grep/read na setkach plików).

## Najbardziej wartościowe use-case (LLM / tokeny)
Na podstawie `docs/ai-perspective-tool-comparison.md` największa przewaga narzędzia jest wtedy, gdy:

### 1) „Irreplaceable” (LLM nie da rady tego policzyć z tekstu)
- **hotspots** (Cognitive Complexity, LOC, nesting) — ranking całego codebase.
- **dead-code** (metody bez callerów) — wymaga semantycznego call graph.
- **coupling/instability** (Ca/Ce/I/A/D) — metryki architektoniczne w skali repo.

### 2) „Faster” (to samo co LLM zrobi, ale dużo taniej)
- **context** (kompaktowa karta metody: CC + callers/callees + cluster + duplicates).
- **tree** (orientacja w strukturze).
- **impact** (transitive callers/callees) — szczególnie w dużych codebase’ach.

### 3) „Inferior / do odchudzenia”
- **token-search / semantic-search na hash embeddingach**: często gorsze niż `grep` + iteracja LLM.
  
Rekomendacja: utrzymać te komendy tylko jako opcjonalne (feature flag / osobny stage), a domyślnie promować: `context`, `hotspots`, `callgraph/impact`, `dead-code`, `coupling`, `drift`.

## Minimalny „flow” dla agenta (context setup)
1. `ai-code-graph analyze <solution.sln>` (lub w CI) → generuje `./ai-code-graph/graph.db`
2. Przed edycją metody: `ai-code-graph context "Namespace.Type.Method" --db ./ai-code-graph/graph.db`
3. Gdy zmiana może mieć blast radius:
   - `ai-code-graph impact "..." --depth 3`
   - `ai-code-graph callgraph "..." --direction both --depth 2`
4. Gdy refactor/cleanup:
   - `ai-code-graph hotspots --top 20 --threshold 10`
   - `ai-code-graph dead-code`
   - `ai-code-graph duplicates --threshold 0.85`

## Co warto dopracować pod „szybkie poruszanie się w kodzie”
- **Token economy jako priorytet**: tryb `--compact` jako default (jedna linia na element, zero „ładnych tabel” jeśli nie trzeba).
- **Stabilne identyfikatory metody** (dla agentów): jednoznaczny „MethodId” + możliwość używania skrótów.
- **Cache invalidation**: wykrywanie, kiedy db jest stale (hash commit + timestamp + sln/inputs).
- **MCP**: narzędzia powinny zwracać krótkie odpowiedzi i mieć sensowne parametry domyślne.

## Co jest już w repo
- Solidny README z listą komend i opisem architektury.
- `pdr.md` jako PDR/PRD v1.
- `.taskmaster/` z istniejącym backlogiem (63+ tasks) — historyczny plan rozwoju.

## Rekomendacja porządkowa
- Trzymać tylko jeden „source of truth” dla roadmapy (Task Master + jeden PRD dla next milestones).
- Benchmark DB (`benchmark/*.db`) traktować jako artifact lokalny (gitignore), nie jako część repo.
