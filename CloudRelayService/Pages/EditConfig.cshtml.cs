using CloudRelayService.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

public class EditConfigModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string AgentId { get; set; }

    // Display-only property for the computer name.
    public string DisplayPrimaryName { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Friendly Name is required.")]
    public string CustomName { get; set; }

    // We are using a string for QueriesInput so that the user can enter multiple queries (one per line).
    [BindProperty]
    public AgentConfigurationViewModel Configuration { get; set; } = new AgentConfigurationViewModel();

    public void OnGet(string agentId)
    {
        AgentId = agentId;
        ReloadAgentFields();
    }

    private void ReloadAgentFields()
    {
        if (AgentHub.Agents.TryGetValue(AgentId, out var agentLocal))
        {
            DisplayPrimaryName = agentLocal.PrimaryName;
            if (string.IsNullOrEmpty(CustomName))
                CustomName = agentLocal.CustomName;
            if (agentLocal.Configuration != null && agentLocal.Configuration.Connections.Any())
            {
                var conn = agentLocal.Configuration.Connections.First();
                Configuration.ConnectionString = conn.ConnectionString;
                // Assume queries are stored one per line; join with newline.
                Configuration.QueriesInput = string.Join(Environment.NewLine, conn.Queries);
            }
        }
        else
        {
            DisplayPrimaryName = "Unknown";
        }
    }

    public async Task<IActionResult> OnPostAsync([FromServices] IHubContext<AgentHub> hubContext)
    {
        if (!ModelState.IsValid)
        {
            ReloadAgentFields();
            return Page();
        }

        // Split the queries from the textarea (one per line) and filter out empty lines.
        var queries = Configuration.QueriesInput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(q => q.Trim())
            .ToList();

        var agentConfig = new AgentConfiguration
        {
            CustomAgentName = CustomName,
            Connections = new List<ConnectionConfig>
        {
            new ConnectionConfig
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionString = Configuration.ConnectionString,
                Queries = queries
            }
        }
        };

        if (AgentHub.Agents.TryGetValue(AgentId, out var agentLocal))
        {
            agentLocal.Configuration = agentConfig;
            agentLocal.CustomName = CustomName;
            // Notify the agent with the updated configuration.
            await hubContext.Clients.Client(agentLocal.ConnectionId).SendAsync("UpdateConfig", agentConfig);
        }
        AgentConfigurationStore.UpdateConfiguration(AgentId, agentConfig);

        var command = Request.Form["command"];
        if (!string.IsNullOrEmpty(command) &&
            string.Equals(command.ToString(), "SaveAndReturn", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("Index");
        }
        return Page();
    }

}

public class AgentConfigurationViewModel
{
    [Required(ErrorMessage = "Connection String is required.")]
    public string ConnectionString { get; set; }

    [Required(ErrorMessage = "Please enter queries.")]
    public string QueriesInput { get; set; }
}
