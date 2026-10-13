using AgenticEngineering.Application.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace AgenticEngineering.Api.Controllers;

[ApiController]
[Route("api/approvals")]
public sealed class ApprovalsController(IWorkflowApprovalService approvalService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<WorkflowApprovalDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowApprovalDto>> Create(
        [FromBody] CreateApprovalRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var approval = await approvalService.RequestAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetAll), approval);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new ProblemDetails { Title = "Workflow not found", Detail = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid approval request", Detail = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Approval prerequisites not met", Detail = exception.Message });
        }
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WorkflowApprovalDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkflowApprovalDto>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await approvalService.GetAllAsync(cancellationToken));
    }

    [HttpPost("{approvalId:guid}/approve")]
    [ProducesResponseType<WorkflowApprovalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<WorkflowApprovalDto>> Approve(
        Guid approvalId,
        [FromBody] ApprovalDecisionDto decision,
        CancellationToken cancellationToken)
        => DecideAsync(approvalId, decision, approve: true, cancellationToken);

    [HttpPost("{approvalId:guid}/reject")]
    [ProducesResponseType<WorkflowApprovalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<WorkflowApprovalDto>> Reject(
        Guid approvalId,
        [FromBody] ApprovalDecisionDto decision,
        CancellationToken cancellationToken)
        => DecideAsync(approvalId, decision, approve: false, cancellationToken);

    private async Task<ActionResult<WorkflowApprovalDto>> DecideAsync(
        Guid approvalId,
        ApprovalDecisionDto decision,
        bool approve,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = approve
                ? await approvalService.ApproveAsync(approvalId, decision, cancellationToken)
                : await approvalService.RejectAsync(approvalId, decision, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid approval decision", Detail = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Approval already decided or unsafe", Detail = exception.Message });
        }
    }
}