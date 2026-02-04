# Output Contract

This document defines the CLI output formats for AI Code Graph, optimized for LLM token economy.

## Method Selection

Commands that operate on methods support two selection modes:

### By Pattern (default)
```bash
ai-code-graph context "ValidateUser"
ai-code-graph callgraph "MyService.Process"
```

### By ID (recommended for agents)
```bash
ai-code-graph context --id "MyApp.Services.UserService.ValidateUser(String)"
ai-code-graph callgraph --id "global::MyApp.Data.Repository.Query(Int32)"
```

**Resolution Precedence:**
1. `--id` — Exact method ID match (fastest, unambiguous)
2. Exact signature — Full qualified name matches exactly
3. Substring match — Pattern found in method name (may be ambiguous)

**Tip:** The `context` command prints the method's ID. Use it once to discover IDs, then use `--id` for subsequent calls to avoid ambiguity.

## Format Options

All query commands support `--format <format>` with these options:

| Format | Description | Default For |
|--------|-------------|-------------|
| `compact` | One line per item, bounded lists, no ASCII tables | Agent-facing commands |
| `table` | Human-readable ASCII tables with headers | Human-facing commands |
| `json` | Stable JSON schema for scripting | - |
| `csv` | Comma-separated values (where applicable) | - |

## Compact Format Rules

The `compact` format is the default for agent-facing commands (`context`, `impact`, `callgraph`, `hotspots`, `dead-code`, `coupling`).

### Core Principles

1. **One item per line** — Each result item occupies exactly one line
2. **Bounded lists** — All lists are capped by `--top` or `--max-items` (default: 20)
3. **Stable identifiers** — Use fully qualified method IDs, not display names
4. **No ASCII art** — No box-drawing characters, separators, or decorative elements
5. **Minimal prefixes** — Use short, consistent prefixes (e.g., `CC:`, `LOC:`, `→`, `←`)

### Default Bounds

| Parameter | Default | Description |
|-----------|---------|-------------|
| `--top` | 20 | Maximum items in ranked lists |
| `--depth` | 2 | Call graph traversal depth |
| `--max-items` | 20 | Maximum items in unbounded sections |

### Compact Output Examples

**hotspots (compact)**
```
MyApp.Services.UserService.ValidateUser(String) CC:12 LOC:35 Nest:3
MyApp.Data.Repository.QueryAll() CC:10 LOC:28 Nest:2
MyApp.Api.AuthController.Login(LoginRequest) CC:8 LOC:22 Nest:2
```

**callgraph (compact)**
```
MyApp.Services.UserService.CreateUser(UserDto)
← AuthController.Register
← AdminService.CreateAdmin
→ UserRepository.Insert
→ PasswordHasher.Hash
→ EventPublisher.Publish
```

**context (compact)**
```
Method: MyApp.Services.UserService.ValidateUser(String)
File: src/Services/UserService.cs:42
CC:12 LOC:35 Nest:3
Callers: AuthController.Login, RegistrationService.Register (+1)
Callees: UserRepository.FindByEmail, PasswordHasher.Verify
Cluster: user-validation (5 members, 0.82)
Duplicates: AccountService.CheckCredentials (0.91)
```

**coupling (compact)**
```
MyApp.Services.UserService Ca:5 Ce:12 I:0.71
MyApp.Data.UserRepository Ca:8 Ce:3 I:0.27
MyApp.Api.AuthController Ca:2 Ce:15 I:0.88
```

**dead-code (compact)**
```
MyApp.Legacy.OldHelper.Unused() — 0 callers
MyApp.Utils.StringExtensions.Obsolete(String) — 0 callers
```

## Table Format

The `table` format uses aligned columns with headers. This is the legacy default and remains available for human readability.

```
Method                                              CC   LOC  Nest  Location
------------------------------------------------------------
MyApp.Services.UserService.ValidateUser(String)    12    35     3  UserService.cs:42
MyApp.Data.Repository.QueryAll()                   10    28     2  Repository.cs:15
```

## JSON Format

JSON output follows a stable schema. Field names use camelCase and remain consistent across versions.

```json
{
  "items": [
    {
      "methodId": "MyApp.Services.UserService.ValidateUser(String)",
      "complexity": 12,
      "loc": 35,
      "maxNesting": 3,
      "location": "src/Services/UserService.cs:42"
    }
  ],
  "metadata": {
    "total": 150,
    "returned": 20,
    "threshold": null
  }
}
```

### JSON Stability Guarantees

- Field names will not change without a major version bump
- New fields may be added (consumers should ignore unknown fields)
- `null` values are included explicitly for optional fields
- Arrays are never `null`; empty arrays are `[]`

## Command Defaults

| Command | Default Format |
|---------|----------------|
| `context` | compact |
| `impact` | compact |
| `callgraph` | compact |
| `hotspots` | compact |
| `dead-code` | compact |
| `coupling` | compact |
| `tree` | table |
| `duplicates` | table |
| `clusters` | table |
| `export` | json |
| `drift` | table |

## Abbreviations

Compact format uses these standard abbreviations:

| Abbreviation | Meaning |
|--------------|---------|
| `CC` | Cognitive Complexity |
| `LOC` | Lines of Code |
| `Nest` | Maximum Nesting Depth |
| `Ca` | Afferent Coupling (incoming dependencies) |
| `Ce` | Efferent Coupling (outgoing dependencies) |
| `I` | Instability (Ce / (Ca + Ce)) |
| `←` | Callers (incoming calls) |
| `→` | Callees (outgoing calls) |
