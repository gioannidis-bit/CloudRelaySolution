using CloudRelayService.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestConnectionController : ControllerBase
    {
        private readonly IHubContext<AgentHub> _hubContext;

        public TestConnectionController(IHubContext<AgentHub> hubContext)
        {
            _hubContext = hubContext;
        }

        // POST api/testconnection/{agentId}/test
        [HttpPost("{agentId}/test")]
        public async Task<IActionResult> TestConnection(string agentId)
        {
            if (!AgentHub.Agents.TryGetValue(agentId, out var agent))
                return NotFound("Agent not found");
            if (!agent.IsOnline)
                return BadRequest("Agent is offline");

            await _hubContext.Clients.Client(agent.ConnectionId).SendAsync("TestConnection");
            return Ok("Test connection command sent to agent.");
        }
    }
}
