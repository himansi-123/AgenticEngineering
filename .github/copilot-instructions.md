# Project Instructions

- Keep the solution a local .NET 8 modular monolith. Preserve project boundaries and dependency direction: API to Application, Infrastructure to Application and Domain, Application to Domain.
- Use asynchronous APIs and pass cancellation tokens through I/O paths.
- Keep HTTP concerns in the API, business rules in Application/Domain, and EF Core details in Infrastructure.
- Use SQLite and deterministic local tests; do not introduce paid services, external LLM keys, shell execution, deployments, or cloud dependencies without explicit approval.
- Treat workflow requirements as incomplete until the scheduler, persistence, approval enforcement, recovery, audit, and scenario tests are implemented. Documentation must distinguish working behavior from planned behavior.
- Run `dotnet build AgenticEngineering.sln` and relevant `dotnet test` projects after changes. Report only validation that actually ran.
