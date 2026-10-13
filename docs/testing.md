# Testing

Run all current tests from the repository root:

```powershell
dotnet test .\AgenticEngineering.sln
```

Unit tests exercise URL scheme/credential rejection and short-code collision retry. API integration tests run against `WebApplicationFactory` with a unique temporary SQLite database per fixture and check health, creation, metadata, 302 redirect, persisted click analytics, unsafe input, and missing codes. They make no external network requests.

## Results

On 2026-10-13, `dotnet build AgenticEngineering.sln --no-restore --verbosity minimal` succeeded with 0 warnings and 0 errors. `dotnet test AgenticEngineering.sln --no-restore --verbosity minimal` passed all 19 unit tests and 10 integration tests.

## Planned test coverage

Workflow tests cover DAG validation/cycle rejection, legal state transitions, independent-task parallelism/dependency synchronization, bounded retries, timeout/cancellation, failure propagation, approval enforcement, persisted audit/metrics, and all three scenarios. Replanning/stale-artifact invalidation and rollback tests remain pending because these features are not implemented.

## Metric definitions

- Workflow success rate: completed runs divided by all terminal runs (Completed, Failed, Cancelled) in the local SQLite store.
- Task failure rate: tasks finally in Failed state divided by all terminal tasks for the requested run. A failure followed by successful retry is not a final task failure.
- Retry frequency: persisted `task.retry_scheduled` events divided by `task.attempt_started` events for the requested run.
- Rollback count: persisted audit events whose type begins with `rollback.`. No rollback operation currently emits these events.
- Mean time to recovery: average time from a task's `task.failed` event to that task's next `task.attempt_started` event; null when no recovery is observed.
- End-to-end latency: terminal run time minus creation time; null for a nonterminal run.

Metrics use recorded data only; empty denominators return null.
