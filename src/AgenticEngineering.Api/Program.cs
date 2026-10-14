using AgenticEngineering.Infrastructure;
using AgenticEngineering.Infrastructure.ShortUrls;
using AgenticEngineering.Application.Workflows;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddShortUrlInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<ShortUrlDbContext>();
    await database.Database.EnsureCreatedAsync();
    var hasExpirationColumn = await database.Database
        .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('ShortUrls') WHERE name = 'ExpiresAtUtc'")
        .SingleAsync();
    if (hasExpirationColumn == 0)
    {
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE ShortUrls ADD COLUMN ExpiresAtUtc TEXT NULL");
    }

    await database.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "WorkflowRuns" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_WorkflowRuns" PRIMARY KEY,
            "OriginalRequirement" TEXT NOT NULL,
            "State" INTEGER NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "UpdatedAtUtc" TEXT NOT NULL,
            "CompletedAtUtc" TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_WorkflowRuns_CreatedAtUtc" ON "WorkflowRuns" ("CreatedAtUtc");
        CREATE TABLE IF NOT EXISTS "WorkflowTasks" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_WorkflowTasks" PRIMARY KEY,
            "RunId" TEXT NOT NULL,
            "Type" INTEGER NOT NULL,
            "Priority" INTEGER NOT NULL,
            "State" INTEGER NOT NULL,
            "AttemptCount" INTEGER NOT NULL,
            "DependenciesJson" TEXT NOT NULL,
            "InputArtifactsJson" TEXT NOT NULL,
            "OutputArtifactsJson" TEXT NOT NULL,
            "Error" TEXT NULL,
            "OutputJson" TEXT NULL,
            "UpdatedAtUtc" TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_WorkflowTasks_RunId" ON "WorkflowTasks" ("RunId");
        CREATE TABLE IF NOT EXISTS "WorkflowAuditEvents" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_WorkflowAuditEvents" PRIMARY KEY AUTOINCREMENT,
            "RunId" TEXT NOT NULL,
            "TaskId" TEXT NULL,
            "EventType" TEXT NOT NULL,
            "PreviousState" TEXT NULL,
            "NewState" TEXT NULL,
            "Detail" TEXT NOT NULL,
            "OccurredAtUtc" TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_WorkflowAuditEvents_RunId_OccurredAtUtc" ON "WorkflowAuditEvents" ("RunId", "OccurredAtUtc");
        CREATE TABLE IF NOT EXISTS "WorkflowApprovalRequests" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_WorkflowApprovalRequests" PRIMARY KEY,
            "RunId" TEXT NOT NULL,
            "Action" INTEGER NOT NULL,
            "Description" TEXT NOT NULL,
            "State" INTEGER NOT NULL,
            "ValidationPassed" INTEGER NOT NULL,
            "SecurityReviewPassed" INTEGER NOT NULL,
            "Reviewer" TEXT NULL,
            "DecisionNotes" TEXT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "DecidedAtUtc" TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_WorkflowApprovalRequests_RunId_State" ON "WorkflowApprovalRequests" ("RunId", "State");
        """);
}

app.Run();

public partial class Program
{
}
