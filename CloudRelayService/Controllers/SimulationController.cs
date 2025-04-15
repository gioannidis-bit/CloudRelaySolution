using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CloudRelayService.Hubs;
using System.Threading.Tasks;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SimulationController : ControllerBase
    {
        private readonly IHubContext<AgentHub> _hubContext;

        public SimulationController(IHubContext<AgentHub> hubContext)
        {
            _hubContext = hubContext;
        }

        // POST /simulate-request
        [HttpPost("simulate-request")]
        public async Task<IActionResult> SimulateRequest()
        {
            // Notify all connected clients (agents) to request data.
            await _hubContext.Clients.All.SendAsync("RequestAgentData");
            return Ok("Request sent to all connected agents.");
        }
    }
}
