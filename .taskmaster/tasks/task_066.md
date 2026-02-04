# Task ID: 66

**Title:** Method identity: introduce stable MethodId in outputs and allow selecting methods via --id

**Status:** pending

**Dependencies:** None

**Priority:** high

**Description:** Reduce ambiguity and make agent tooling deterministic.

**Details:**

1) Ensure every method printed includes a stable MethodId.
2) Add `--id <MethodId>` to commands that take a method pattern.
3) Document selection precedence: --id > exact signature > substring match (with disambiguation).

**Test Strategy:**

Tests: method overloads produce different ids; `--id` resolves correctly; ambiguous patterns return a clear error + suggestions.

## Subtasks

### 66.1. Include MethodId in models

**Status:** pending  
**Dependencies:** None  

Ensure a stable MethodId is present and printed.

**Details:**

Audit current MethodModel id generation; ensure it is stable and included in all relevant outputs.

### 66.2. Add --id resolution path

**Status:** pending  
**Dependencies:** 66.1  

Allow users/agents to select a method by id.

**Details:**

Add `--id <MethodId>` to commands that accept method patterns; implement resolution precedence and disambiguation.

### 66.3. Update docs/examples to prefer ids

**Status:** pending  
**Dependencies:** 66.2  

Teach agents to use ids to avoid ambiguity.

**Details:**

Update quickstart/examples to show id usage when available.
