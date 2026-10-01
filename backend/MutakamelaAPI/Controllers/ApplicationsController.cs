using Microsoft.AspNetCore.Mvc;
using MutakamelaAPI.Applications;
using MutakamelaAPI.Browser;
using MutakamelaAPI.Models;

namespace MutakamelaAPI.Controllers;

/// <summary>
/// Job endpoints for the agentic engine: the widget polls these to render the
/// status/review card, and staff tooling can list jobs. Every state change that
/// matters (approve, cancel, login) is an explicit call; nothing auto-advances
/// to a submit.
/// </summary>
[ApiController]
[Route("api/applications")]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationOrchestrator _orchestrator;
    private readonly IFlowRegistry _flows;

    public ApplicationsController(IApplicationOrchestrator orchestrator, IFlowRegistry flows)
    {
        _orchestrator = orchestrator;
        _flows = flows;
    }

    /// <summary>Journeys the engine knows about, with their field lists (no selectors).</summary>
    [HttpGet("flows")]
    public IActionResult GetFlows() => Ok(_flows.All.Select(f => new
    {
        f.Id, f.Title, f.TitleAr, f.Url, f.Kind, f.RequiresLogin, f.RequiresApproval, f.Verified,
        Fields = f.AllFields.Select(x => new { x.Id, x.Label, x.LabelAr, x.Type, x.Required, x.Source })
    }));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? sessionId, [FromQuery] int limit = 50) =>
        Ok(await _orchestrator.ListAsync(sessionId, Math.Clamp(limit, 1, 200)));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var job = await _orchestrator.GetAsync(id);
        return job == null ? NotFound() : Ok(job);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.FlowId))
            return BadRequest(new { error = "sessionId and flowId are required" });
        if (_flows.Get(request.FlowId) == null)
            return BadRequest(new { error = $"Unknown flowId. Known: {string.Join(", ", FlowIds.All)}" });
        var lang = Lang(request.Language);
        var reply = await _orchestrator.StartAsync(request.SessionId, request.FlowId, lang, request.Data);
        return Ok(new { reply.Stage, reply.Response, reply.ResponseAr, Application = reply.Application });
    }

    [HttpPatch("{id}/data")]
    public async Task<IActionResult> UpdateData(string id, [FromBody] UpdateApplicationDataRequest request, [FromQuery] string language = "en")
    {
        var job = await _orchestrator.ApplyDataAsync(id, request.Data, Lang(language));
        return job == null ? NotFound() : Ok(job);
    }

    [HttpPost("{id}/login-complete")]
    public async Task<IActionResult> LoginComplete(string id, [FromQuery] string language = "en")
    {
        var job = await _orchestrator.MarkLoginCompleteAsync(id, Lang(language));
        return job == null ? NotFound() : Ok(job);
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id, [FromQuery] string language = "en")
    {
        var job = await _orchestrator.ApproveAsync(id, Lang(language));
        return job == null ? NotFound() : Ok(job);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id, [FromQuery] string language = "en")
    {
        var job = await _orchestrator.CancelAsync(id, Lang(language));
        return job == null ? NotFound() : Ok(job);
    }

    private static string Lang(string? language) =>
        string.Equals(language?.Trim(), "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
}
