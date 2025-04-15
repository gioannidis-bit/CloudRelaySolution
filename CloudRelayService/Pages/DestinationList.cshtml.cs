using CloudRelayService.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Generic;

public class DestinationListModel : PageModel
{
    public List<DestinationConfig> Destinations { get; set; }

    public void OnGet()
    {
        Destinations = DestinationStore.LoadDestinations();
    }
}
