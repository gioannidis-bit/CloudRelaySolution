using CloudRelayService.Hubs;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Generic;
using System.Linq;

public class IndexModel : PageModel
{
    public List<AgentInfo> Agents { get; set; } = new List<AgentInfo>();

    public void OnGet()
    {
        Agents = AgentHub.Agents.Values.ToList();
    }
}
