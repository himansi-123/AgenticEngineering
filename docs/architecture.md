# Architecture

## Current implementation

The solution is a modular monolith with dependency direction kept inward:

```text
Api -> Application -> Domain
Api -> Infrastructure -> Application + Domain
```

The API owns HTTP status mapping and request validation. `IShortUrlService` implements destination checks, code-collision retries, and DTO mapping. `IShortUrlRepository` isolates persistence. Infrastructure configures EF Core SQLite and stores short URLs plus click events. The API startup uses `EnsureCreated` so the local prototype runs without a migration command.

### URL request flow

1. `POST /api/urls` binds and bounds the request DTO.
2. Application accepts absolute HTTP/HTTPS URLs only and rejects user-info credentials.
3. A cryptographically random code is generated; repository uniqueness constraints are retried up to five times.
4. Metadata is read from SQLite. `GET /{code}` writes a click timestamp before issuing a 302.
5. Analytics returns the recorded click timestamps and count.

## Workflow engine: implemented and planned

`WorkflowPlanFactory` creates typed tasks, priorities, explicit dependencies, and artifact references. `WorkflowTaskGraph` rejects duplicate/empty IDs, duplicate/unknown dependencies, self-dependencies, and cycles, then returns a deterministic topological order. `WorkflowRun` and `WorkflowTask` enforce legal transitions. `DagWorkflowScheduler` makes tasks Ready only when dependencies succeed, launches independent tasks up to its concurrency ceiling, and synchronizes dependents. It applies bounded exponential retry to transient failures, handles timeouts/cancellation, skips downstream tasks after failure, and continues independent branches. A callback persists task transitions and results; SQLite writes are serialized separately from concurrent agent execution.

Workflow runs, task states/outputs, approvals, and audit events persist in SQLite. Deterministic stages cover analysis, clarification, decomposition, architecture, implementation planning, validation, documentation, security review, release readiness, and summary. Artifact references identify task-output records, not generated files. The validation agent does not run repository tests. Approval requests require successful validation and security-review tasks; `WorkflowApprovalGate` refuses protected callbacks without approval and evidence. No code-application, migration, or deployment callback is wired.

Workflow creation is synchronous in the HTTP request. State survives process restarts, but interrupted runs are not automatically resumed. Dynamic replanning, stale-output invalidation, artifact versioning, rollback, and human recovery handoff remain unimplemented.
