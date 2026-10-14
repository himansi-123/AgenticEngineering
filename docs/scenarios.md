# Demonstration Scenarios

The three inputs in `samples/scenarios.json` run through `POST /api/scenarios/{scenarioId}/run`. Each execution persists a task graph, deterministic structured outputs, and audit history.

## Greenfield: configurable expiration

Requirement: Add configurable URL expiration to shortened URLs. Expiration is optional per link, must be later than creation, existing links without expiration remain permanent, and expired links return HTTP 410 without recording clicks. The implementation task creates a short-lived URL through the allowlisted URL application service; validation reads its persisted state, records one active click, waits for expiry, and confirms the expired access does not increase analytics.

## Brownfield: analytics defect

Requirement: Investigate the suspected analytics-counting defect. The scenario persists a short URL and two clicks, then verifies the SQLite-backed count and timestamps. The current implementation reports both clicks correctly, so no source-level defect or fix is claimed. A separate integration test injects a transient validation failure to test retry and audit persistence; that injected test failure is not evidence of an analytics defect.

## Ambiguous: automatic expiration

Requirement: “Make links expire automatically.” The workflow persists four unresolved decisions, enters `AwaitingClarification`, and leaves implementation tasks pending. `POST /api/workflows/{runId}/clarifications` records a non-empty response and resumes from persisted task state; the supported optional-per-link/HTTP-410 policy then runs the same behavior checks as the greenfield scenario.

## Current status

All three scenarios are covered by behavior-focused integration tests. Execution is restricted to the explicitly registered URL application service; arbitrary shell and filesystem access are not exposed. Graph visualization, general workspace code editing, rollback, deployment, and automatic process-restart recovery are not provided. Approval requests can be decided after required stages, but no source-code or deployment side effect is connected to the approval gate.
