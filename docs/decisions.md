# Decisions and Trade-offs

- **.NET 8:** The installed environment provides SDK 8.0.425 and ASP.NET Core runtime 8.0.31, so the requested supported LTS stack is available.
- **Modular monolith:** Domain, application contracts/use cases, infrastructure, and API are separate projects in one deployable solution. This keeps local operation simple while leaving persistence and agent providers replaceable.
- **SQLite and EF Core:** A local file database provides persistence without external services. Startup uses `EnsureCreated` plus additive workflow-table creation to preserve the first milestone's URL data. Production schema evolution should use versioned migrations.
- **Deterministic code generation:** Short codes use cryptographic random bytes rendered as hex. A unique database key is authoritative, and the application makes at most five attempts.
- **Redirect policy:** Only absolute HTTP and HTTPS destinations without embedded credentials are accepted. This limits unsafe schemes, but is not a full anti-abuse or destination-trust policy.
- **No LLM dependency:** No external AI provider is used. Deterministic agents return structured results for all lifecycle stages.
- **Plan-only implementation:** The implementation stage does not alter source files. This avoids claiming autonomous code application without artifact versioning, review diffs, and rollback safeguards.
- **No arbitrary command execution:** The application executes no generated shell commands. The deterministic validation stage checks result structure; repository tests are run separately by `dotnet test`.
- **Approval boundary:** Approval requests and the protected-action gate are implemented, but no code, migration, release, or deployment side effect is connected to the gate.
- **Known workflow gaps:** Dynamic replanning, stale-artifact invalidation, rollback, interactive clarification, and automatic process-restart recovery are not implemented. Scenario recovery demonstrates a controlled retry, not arbitrary code rollback.
