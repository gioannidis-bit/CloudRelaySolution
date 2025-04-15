using CloudRelayService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

public class DestinationConfigModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? DestinationId { get; set; }

    [BindProperty]
    public DestinationConfigurationViewModel Configuration { get; set; } = new DestinationConfigurationViewModel();

    public void OnGet(string destinationId)
    {
        DestinationId = destinationId;
        if (!string.IsNullOrEmpty(DestinationId))
        {
            var destination = DestinationStore.Destinations.Find(d => d.Id == DestinationId);
            if (destination != null)
            {
                var builder = new SqlConnectionStringBuilder(destination.ConnectionString);
                var dataSourceParts = builder.DataSource.Split('\\');
                Configuration.HostOrIp = dataSourceParts[0];
                Configuration.Instance = dataSourceParts.Length > 1 ? dataSourceParts[1] : "MSSQLSERVER";
                Configuration.Database = builder.InitialCatalog;
                // Do not prefill sensitive credentials.
                Configuration.Username = "";
                Configuration.Password = "";
               
            }
        }
        else
        {
            // Set default values for new entry.
            Configuration.Instance = "MSSQLSERVER";
            Configuration.Port = 1433;
            Configuration.Database = "master";
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Debug: check which command was pressed (Save or SaveAndReturn)
        var cmd = Request.Form["command"].ToString();
        Console.WriteLine($"[DEBUG] command = {cmd}");

        // After model binding but before your logic:
        if (!ModelState.IsValid)
        {
            Console.WriteLine("[DEBUG] ModelState is invalid; returning Page().");
            // Print each error
            foreach (var kvp in ModelState)
            {
                foreach (var err in kvp.Value.Errors)
                {
                    Console.WriteLine($"[DEBUG] Field '{kvp.Key}' error: {err.ErrorMessage}");
                }
            }
            return Page();
        }

       
        // Build the destination connection string.
        var builder = new SqlConnectionStringBuilder();
        builder.DataSource = string.IsNullOrWhiteSpace(Configuration.Instance)
            ? $"{Configuration.HostOrIp},{Configuration.Port}"
            : $"{Configuration.HostOrIp}\\{Configuration.Instance},{Configuration.Port}";
        builder.InitialCatalog = Configuration.Database;
        if (!string.IsNullOrWhiteSpace(Configuration.Username) || !string.IsNullOrWhiteSpace(Configuration.Password))
        {
            builder.UserID = Configuration.Username;
            builder.Password = Configuration.Password;
            builder.IntegratedSecurity = false;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        // Ορίζουμε πάντα να χρησιμοποιείται TrustServerCertificate = true
        builder.TrustServerCertificate = true;

        string connString = builder.ConnectionString;

        DestinationConfig dest = null;
        if (!string.IsNullOrEmpty(DestinationId))
        {
            dest = DestinationStore.Destinations.Find(d => d.Id == DestinationId);
        }
        if (dest == null)
        {
            dest = new DestinationConfig { Id = Guid.NewGuid().ToString() };
            DestinationStore.Destinations.Add(dest);
        }
        dest.ConnectionString = connString;
        

        DestinationStore.SaveDestinations();
        TempData["Message"] = "Destination configuration saved.";

        
        if (cmd.Equals("SaveAndReturn", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("DestinationList");
        }
        return Page();
    }
}


public class DestinationConfigurationViewModel
{
    [Required(ErrorMessage = "Host or IP is required.")]
    public string HostOrIp { get; set; }

    // Default instance is "MSSQLSERVER" (user can change if needed)
    public string Instance { get; set; } = "MSSQLSERVER";

    // Use default port 1433; not marked as required because a default is provided.
    public int Port { get; set; } = 1433;

    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; set; }

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; }

    // Not required; if empty we will default to "master"
    public string Database { get; set; } = "master";

    

    // Option to create a new database; if not checked, Database is used.
   



    // Optional: a table name to use for data operations.
    
}
