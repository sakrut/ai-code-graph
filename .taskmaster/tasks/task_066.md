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
