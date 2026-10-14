# Agentic Engineering

A local .NET 8 prototype for turning software requirements into reviewable engineering outcomes. The implementation is being delivered incrementally; this repository currently contains the first runnable milestone, not the complete orchestration system.

## Implemented now

- ASP.NET Core Web API with Swagger in Development and `GET /health`.
- URL creation, unique short-code generation, URL metadata, redirects, and persisted click timestamps.
- Optional per-link URL expiration persisted in SQLite; expired links return `410 Gone` and do not record clicks.
- SQLite storage through EF Core. The database is created on startup with `EnsureCreated` for this prototype.
- HTTP/HTTPS-only destination validation; URLs containing embedded credentials are rejected.
- xUnit unit tests and API integration tests using isolated SQLite databases.
- Persisted workflow runs, dependency graphs, task results, approvals, and audit events in SQLite.
- DAG scheduling with bounded concurrency, dependency gating, bounded retries, per-task timeouts, cancellation, clarification pauses, and dependent-task skipping.
- Deterministic structured stages for analysis, clarification, decomposition, design, implementation planning, validation, documentation, security, release readiness, and summary.
- Runnable greenfield, brownfield, and ambiguous scenarios with persisted URL behavior checks and clarification pause/resume; bounded retries are separately tested with an explicitly injected test failure.
- Workflow execution is limited to explicitly registered in-process application capabilities; no arbitrary shell or filesystem execution is available.

## Important limitations

There is no general-purpose workspace code-editing, shell, migration, or deployment executor. The expiration workflow uses an allowlisted URL application-service operation and validates persisted behavior; unsupported implementation requests fail rather than being reported as implemented. The brownfield scenario verifies persisted click analytics; no source-level counting defect was reproduced or fixed. Test-only failure injection exercises retry/audit behavior separately and does not claim an analytics defect. Approval requests and `WorkflowApprovalGate` are implemented, but no protected source-code or deployment side effect is connected. Dynamic replanning/stale-output invalidation, rollback, and automatic resumption after process restart remain unimplemented.

## Requirements

- .NET SDK 8.0.x
- Windows, Linux, or macOS

## Build, test, and run

From the repository root:

```powershell
dotnet restore .\AgenticEngineering.sln
dotnet build .\AgenticEngineering.sln
dotnet test .\AgenticEngineering.sln
dotnet run --project .\src\AgenticEngineering.Api\AgenticEngineering.Api.csproj
```

The default database is `agentic-engineering.db` in the process working directory. Override it with `ConnectionStrings__ShortUrls`, for example:

```powershell
$env:ConnectionStrings__ShortUrls = 'Data Source=local-shorturls.db'
dotnet run --project .\src\AgenticEngineering.Api\AgenticEngineering.Api.csproj
```

Swagger is available at `/swagger` in Development. The health endpoint is `/health`.

## URL API

- `POST /api/urls` with `{ "url": "https://example.com/path", "expiresAtUtc": "2030-01-01T00:00:00Z" }` returns `201 Created` and URL metadata. `expiresAtUtc` is optional and must be in the future.
- `GET /api/urls/{code}` returns metadata or `404`.
- `GET /{code}` records a click timestamp and returns a `302` redirect, or `404`.
- `GET /api/urls/{code}/analytics` returns the total and click timestamps, or `404`.

Example:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/urls -ContentType 'application/json' -Body '{"url":"https://example.com"}'
```

Use the actual URL printed by `dotnet run`; its port may differ from the example.

## Workflow API

- `POST /api/workflows` with `{ "requirement": "..." }` creates and synchronously executes a bounded workflow; unresolved ambiguity returns `AwaitingClarification`, not success.
- `POST /api/workflows/{runId}/clarifications` with `{ "response": "..." }` persists the response and resumes a paused workflow.
- `GET /api/workflows/{runId}` returns state and summary; `/tasks`, `/audit`, and `/metrics` expose persisted execution details.
- `GET /api/approvals` lists requests; `POST /api/approvals` requests approval after required validation/security stages pass.
- `POST /api/approvals/{approvalId}/approve` and `/reject` persist reviewer decisions.
- `GET /api/scenarios` lists reproducible scenarios; `POST /api/scenarios/{scenarioId}/run` executes one.

An approval decision does not itself execute a protected operation; future action adapters must call the gate immediately before performing side effects. Clarification responses are required before an ambiguous workflow proceeds to implementation.

## Solution layout

- `src/AgenticEngineering.Api`: HTTP endpoints and composition root.
- `src/AgenticEngineering.Application`: URL/workflow use cases, agent contracts, and DAG scheduler.
- `src/AgenticEngineering.Domain`: URL, workflow, approval, and audit entities plus graph/state rules.
- `src/AgenticEngineering.Infrastructure`: EF Core SQLite repositories.
- `tests/AgenticEngineering.UnitTests`: application behavior tests.
- `tests/AgenticEngineering.IntegrationTests`: API tests over SQLite.
- `docs/`: architecture, scenarios, testing, and decisions.
- `samples/`: reproducible requirement inputs for planned workflow scenarios.

## Current validation

On 2026-10-13, `dotnet build AgenticEngineering.sln --no-restore --verbosity minimal` succeeded with 0 warnings and 0 errors. The unit test project passed all 22 tests and the integration test project passed all 16 tests.

## Security and limitations

The API has no authentication, authorization, rate limiting, or tenant separation. Reviewer identity is caller-supplied. Workflow execution is synchronous; persisted state is not automatically resumed after restart. Click, approval, and audit lists are unpaginated. SQLite startup uses `EnsureCreated` plus additive workflow-table creation; migration history and an approved migration executor are absent. URL validation restricts schemes but does not establish that a destination is trustworthy. Do not expose this prototype publicly without additional abuse controls.

## Publishing

To publish, create a GitHub repository, add it as the `origin` remote, and push the default branch. Repository creation and a public URL are not part of this local prototype, so no remote URL is claimed here.
