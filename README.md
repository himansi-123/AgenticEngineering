# Agentic Engineering

A local .NET 8 prototype for turning software requirements into reviewable engineering outcomes. The implementation is being delivered incrementally; this repository currently contains the first runnable milestone, not the complete orchestration system.

## Implemented now

- ASP.NET Core Web API with Swagger in Development and `GET /health`.
- URL creation, unique short-code generation, URL metadata, redirects, and persisted click timestamps.
- SQLite storage through EF Core. The database is created on startup with `EnsureCreated` for this prototype.
- HTTP/HTTPS-only destination validation; URLs containing embedded credentials are rejected.
- xUnit unit tests and API integration tests using isolated SQLite databases.
- Persisted workflow runs, dependency graphs, task results, approvals, and audit events in SQLite.
- DAG scheduling with bounded concurrency, dependency gating, retries, per-task timeouts, cancellation, and dependent-task skipping.
- Deterministic structured stages for analysis, clarification, decomposition, design, implementation planning, validation, documentation, security, release readiness, and summary.
- Runnable greenfield, brownfield, and ambiguous scenarios, including one audited transient failure and retry.

## Important limitations

The workflow implementation stage produces a plan only: it does not modify source files, invoke shell commands, or run repository tests. Approval requests and `WorkflowApprovalGate` are implemented, but no code-application, migration, or deployment action is connected to execute. Dynamic replanning/stale-output invalidation, rollback, automatic resumption after process restart, and interactive clarification remain unimplemented.

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

- `POST /api/urls` with `{ "url": "https://example.com/path" }` returns `201 Created` and URL metadata.
- `GET /api/urls/{code}` returns metadata or `404`.
- `GET /{code}` records a click timestamp and returns a `302` redirect, or `404`.
- `GET /api/urls/{code}/analytics` returns the total and click timestamps, or `404`.

Example:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/urls -ContentType 'application/json' -Body '{"url":"https://example.com"}'
```

Use the actual URL printed by `dotnet run`; its port may differ from the example.

## Workflow API

- `POST /api/workflows` with `{ "requirement": "..." }` creates and synchronously executes a deterministic workflow.
- `GET /api/workflows/{runId}` returns state and summary; `/tasks`, `/audit`, and `/metrics` expose persisted execution details.
- `GET /api/approvals` lists requests; `POST /api/approvals` requests approval after required validation/security stages pass.
- `POST /api/approvals/{approvalId}/approve` and `/reject` persist reviewer decisions.
- `GET /api/scenarios` lists reproducible scenarios; `POST /api/scenarios/{scenarioId}/run` executes one.

An approval decision does not itself execute a protected operation; future action adapters must call the gate immediately before performing side effects.

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

On 2026-10-13, `dotnet build AgenticEngineering.sln --no-restore --verbosity minimal` succeeded with 0 warnings and 0 errors. `dotnet test AgenticEngineering.sln --no-restore --verbosity minimal` passed all 19 unit tests and 10 integration tests.

## Security and limitations

The API has no authentication, authorization, rate limiting, or tenant separation. Reviewer identity is caller-supplied. Workflow execution is synchronous; persisted state is not automatically resumed after restart. Click, approval, and audit lists are unpaginated. SQLite startup uses `EnsureCreated` plus additive workflow-table creation; migration history and an approved migration executor are absent. URL validation restricts schemes but does not establish that a destination is trustworthy. Do not expose this prototype publicly without additional abuse controls.

## Publishing

To publish, create a GitHub repository, add it as the `origin` remote, and push the default branch. Repository creation and a public URL are not part of this local prototype, so no remote URL is claimed here.
