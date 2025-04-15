using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SqlInfoController : ControllerBase
    {
        // GET: api/sqlinfo/databases?host=...&instance=...&port=...&username=...&password=...
        [HttpGet("databases")]
        public async Task<IActionResult> GetDatabases(
            [FromQuery] string host,
            [FromQuery] string? instance,
            [FromQuery] int port,
            [FromQuery] string username,
            [FromQuery] string password)
        {
            // Build a connection string that connects to the "master" database.
            var builder = new SqlConnectionStringBuilder();
            // Use specified instance if provided.
            builder.DataSource = string.IsNullOrWhiteSpace(instance)
                ? $"{host},{port}"
                : $"{host}\\{instance},{port}";
            builder.InitialCatalog = "master";
            if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
            {
                builder.UserID = username;
                builder.Password = password;
                builder.IntegratedSecurity = false;
            }
            else
            {
                builder.IntegratedSecurity = true;
            }
            // Optionally, trust the server certificate.
            builder.TrustServerCertificate = true;

            try
            {
                using (var sqlConn = new SqlConnection(builder.ConnectionString))
                {
                    await sqlConn.OpenAsync();
                    using (var command = new SqlCommand("SELECT name FROM sys.databases ORDER BY name", sqlConn))
                    {
                        var dt = new DataTable();
                        dt.Load(await command.ExecuteReaderAsync());
                        var jsonResult = JsonConvert.SerializeObject(dt);
                        return Ok(jsonResult);
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error retrieving databases: " + ex.Message);
            }
        }

        // POST: api/sqlinfo/createdatabase
        // The JSON body must contain: host, instance, port, username, password, and NewDatabaseName.
      
    }

    public class CreateDatabaseRequest
    {
        public string Host { get; set; }
        public string? Instance { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string? NewDatabaseName { get; set; }
    }
}
