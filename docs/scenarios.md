# Demonstration Scenarios

The three inputs in `samples/scenarios.json` run through `POST /api/scenarios/{scenarioId}/run`. Each execution persists a task graph, deterministic structured outputs, and audit history.

## Greenfield: configurable expiration

Requirement: Add configurable expiration to shortened URLs. Analysis records open decisions about default lifetime, per-link configurability, existing links, and expired-link responses, then produces design, implementation-plan, validation, documentation, security, release-review, and summary tasks. It does not add expiration behavior to the URL API.

## Brownfield: analytics defect

Requirement: Fix an analytics-counting defect in the URL shortener. The deterministic scenario injects one transient failure in validation, retries once, and retains the failed attempt and retry in audit history. It does not inspect or modify source code or run regression tests; it demonstrates recovery mechanics only.

## Ambiguous: automatic expiration

Requirement: “Make links expire automatically.” Analysis and clarification tasks identify default expiration, configurability, existing-link treatment, and expired-link behavior without silently selecting defaults. Questions are recorded in task output; there is not yet an interactive clarification API.

## Current status

All three scenarios are runnable and covered by integration tests. Graph visualization, interactive clarification, source-code changes, rollback, and deployment are not provided. Approval requests can be decided after required stages, but no protected side effect is connected to the approval gate.
