using CloudRelayService.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using System.Threading.Tasks;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QueryController : ControllerBase
    {
        private readonly IHubContext<AgentHub> _hubContext;
        public QueryController(IHubContext<AgentHub> hubContext)
        {
            _hubContext = hubContext;
        }

        // POST api/query/{agentId}/run?queryIndex=0
        [HttpPost("{agentId}/run")]
        public async Task<IActionResult> RunQuery(string agentId, [FromQuery] int? queryIndex)
        {
            if (!AgentHub.Agents.TryGetValue(agentId, out var agent))
                return NotFound("Agent not found");

            if (!agent.IsOnline)
                return BadRequest("Agent is offline");

            // Pass the query index as a string parameter. If null, send an empty string.
            string indexParam = queryIndex.HasValue ? queryIndex.Value.ToString() : "";
            await _hubContext.Clients.Client(agent.ConnectionId).SendAsync("GetData", indexParam);
            return Ok("Query command sent to agent with query index: " + indexParam);
        }
    }
}
