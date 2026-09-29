using Microsoft.AspNetCore.Mvc;
using MutakamelaAPI.Models;
using MutakamelaAPI.Services;

namespace MutakamelaAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ISessionManager _sessionManager;

    public HealthController(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet]
    public ActionResult<HealthResponse> GetHealth()
    {
        return Ok(new HealthResponse
        {
            Status = "healthy",
            Service = "Mutakamela Insurance API",
            ActiveSessions = _sessionManager.GetActiveSessionCount(),
            Timestamp = DateTime.UtcNow
        });
    }
}
