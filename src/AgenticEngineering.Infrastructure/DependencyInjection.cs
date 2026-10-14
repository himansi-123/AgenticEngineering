using AgenticEngineering.Application.ShortUrls;
using AgenticEngineering.Application.Workflows;
using AgenticEngineering.Infrastructure.ShortUrls;
using AgenticEngineering.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticEngineering.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddShortUrlInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ShortUrls")
            ?? "Data Source=agentic-engineering.db";

        services.AddDbContextFactory<ShortUrlDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IShortUrlRepository, SqliteShortUrlRepository>();
        services.AddScoped<IShortUrlService, ShortUrlService>();
        services.AddSingleton<IShortCodeGenerator, RandomShortCodeGenerator>();
        services.AddScoped<IWorkflowTaskExecutor, RuleBasedWorkflowTaskExecutor>();
        services.AddSingleton<WorkflowPlanFactory>();
        var maxConcurrency = int.TryParse(configuration["Workflow:MaxConcurrency"], out var configuredConcurrency)
            ? configuredConcurrency
            : 3;
        var maxRetries = int.TryParse(configuration["Workflow:MaxRetries"], out var configuredRetries)
            ? configuredRetries
            : 1;
        services.AddSingleton(new WorkflowSchedulerOptions(MaxConcurrency: maxConcurrency, MaxRetries: maxRetries));
        services.AddScoped<DagWorkflowScheduler>();
        services.AddScoped<IWorkflowRepository, SqliteWorkflowRepository>();
        services.AddScoped<IWorkflowApprovalRepository, SqliteWorkflowApprovalRepository>();
        services.AddScoped<IWorkflowApprovalService, WorkflowApprovalService>();
        services.AddScoped<WorkflowApprovalGate>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        return services;
    }
}