using AgenticEngineering.Application.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace AgenticEngineering.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public sealed class WorkflowsController(IWorkflowService workflowService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<WorkflowRunDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowRunDto>> Create(
        [FromBody] CreateWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await workflowService.CreateAndExecuteAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { runId = run.Id }, run);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid workflow requirement",
                Detail = exception.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    [HttpGet("{runId:guid}")]
    [ProducesResponseType<WorkflowRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowRunDto>> Get(Guid runId, CancellationToken cancellationToken)
    {
        var run = await workflowService.GetAsync(runId, cancellationToken);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("{runId:guid}/tasks")]
    [ProducesResponseType<IReadOnlyList<WorkflowTaskDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<WorkflowTaskDto>>> GetTasks(Guid runId, CancellationToken cancellationToken)
    {
        var tasks = await workflowService.GetTasksAsync(runId, cancellationToken);
        return tasks is null ? NotFound() : Ok(tasks);
    }

    [HttpGet("{runId:guid}/audit")]
    [ProducesResponseType<IReadOnlyList<WorkflowAuditEventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<WorkflowAuditEventDto>>> GetAudit(Guid runId, CancellationToken cancellationToken)
    {
        var events = await workflowService.GetAuditAsync(runId, cancellationToken);
        return events is null ? NotFound() : Ok(events);
    }

    [HttpGet("{runId:guid}/metrics")]
    [ProducesResponseType<WorkflowMetricsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowMetricsDto>> GetMetrics(Guid runId, CancellationToken cancellationToken)
    {
        var metrics = await workflowService.GetMetricsAsync(runId, cancellationToken);
        return metrics is null ? NotFound() : Ok(metrics);
    }
}