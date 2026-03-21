using Hangfire; // Don't forget this using statement!
using Microsoft.AspNetCore.Mvc;
using Temperance.Constellations.Models.Backtest;
using Temperance.Constellations.Services.Interfaces;

namespace Temperance.Constellations.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BacktestOrchestratorController : ControllerBase
    {
        private readonly IBackgroundJobClient _backgroundJobClient;

        // We must inject the Hangfire client here
        public BacktestOrchestratorController(IBackgroundJobClient backgroundJobClient)
        {
            _backgroundJobClient = backgroundJobClient;
        }

        [HttpPost("begin-backtest")]
        public IActionResult Start([FromBody] MasterBacktestRequest request)
        {
            // Validate the request for systematic integrity
            if (request == null || request.StartDate >= request.EndDate)
            {
                return BadRequest("Invalid backtest parameters. Temporal causality must be maintained.");
            }

            // Hangfire enqueues the background worker using the body data
            _backgroundJobClient.Enqueue<IMasterBacktestRunner>(runner =>
                runner.ExecuteFullSessionAsync(request.SessionId, request.StartDate, request.EndDate));

            return Accepted(new
            {
                Message = "Master backtest session initiated via secure payload.",
                SessionId = request.SessionId,
                StartedAt = DateTime.UtcNow
            });
        }
    }
}