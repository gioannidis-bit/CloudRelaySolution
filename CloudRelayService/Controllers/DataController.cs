using CloudRelayService.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DataController : ControllerBase
    {
        // GET api/data/push?agentId=xxx&destId=yyy
        [HttpGet("push")]
        public async Task<IActionResult> PushData([FromQuery] string agentId, [FromQuery] string destId)
        {
            // Εύρεση του agent.
            if (!AgentHub.Agents.TryGetValue(agentId, out var agent))
                return NotFound("Agent not found");

            if (string.IsNullOrEmpty(agent.LatestData))
                return BadRequest("No data available for this agent");

            // Εύρεση της ρύθμισης προορισμού.
            var dest = DestinationStore.Destinations.FirstOrDefault(d => d.Id == destId);
            if (dest == null)
                return NotFound("Destination configuration not found");

            // Εισαγωγή ή ενημέρωση των δεδομένων στον προορισμό SQL.
            try
            {
                using (var conn = new SqlConnection(dest.ConnectionString))
                {
                    await conn.OpenAsync();
                    string commandText = $@"
IF EXISTS (SELECT 1 FROM {dest.TableName} WHERE AgentId = @AgentId)
    UPDATE {dest.TableName} SET JsonData = @JsonData WHERE AgentId = @AgentId;
ELSE
    INSERT INTO {dest.TableName} (AgentId, JsonData) VALUES (@AgentId, @JsonData);";

                    using (var command = new SqlCommand(commandText, conn))
                    {
                        command.Parameters.AddWithValue("@AgentId", agentId);
                        command.Parameters.AddWithValue("@JsonData", agent.LatestData);
                        await command.ExecuteNonQueryAsync();
                    }
                }

                return Ok("Data pushed to destination successfully");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error pushing data: " + ex.Message);
            }
        }
    }
}
