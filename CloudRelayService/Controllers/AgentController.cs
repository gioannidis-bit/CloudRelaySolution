using CloudRelayService.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using System.Threading.Tasks;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("api/agent")]
    public class AgentController : ControllerBase
    {
        // GET: api/agent
        [HttpGet]
        public IActionResult GetAgents()
        {
            var agents = AgentHub.Agents.Values.ToList();
            return Ok(agents);
        }

        // POST: api/agent/{agentId}
        // Expects JSON with the configuration that contains connection settings and the custom friendly name.
        [HttpPost("{agentId}")]
        public async Task<IActionResult> UpdateAgentConfiguration(string agentId, [FromBody] AgentConfiguration config, [FromServices] IHubContext<AgentHub> hubContext)
        {
            if (AgentHub.Agents.TryGetValue(agentId, out var agent))
            {
                agent.Configuration = config;
                // Update the friendly name (from the configuration's CustomAgentName)
                agent.CustomName = config.CustomAgentName;
                // Optionally, send an update to the client (for auto-refresh)
                await hubContext.Clients.Client(agent.ConnectionId).SendAsync("UpdateConfig", config);
                return Ok(agent);
            }
            return NotFound("Agent not found");
        }
    }
}
