using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AgentConfigApiController : ControllerBase
    {
        // GET: /AgentConfigApi/TestConnection?connectionString=yourConnectionString
        [HttpGet("TestConnection")]
        public async Task<IActionResult> TestConnection([FromQuery] string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return BadRequest("Connection string is required.");
            }

            var tableList = new List<string>();

            try
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // Retrieve all tables and views
                    string sql = @"
                        SELECT TABLE_NAME, TABLE_TYPE 
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')
                        ORDER BY TABLE_NAME;";

                    using (var command = new SqlCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string name = reader["TABLE_NAME"].ToString();
                                string type = reader["TABLE_TYPE"].ToString();
                                tableList.Add($"{name} ({type})");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return BadRequest("Error: " + ex.Message);
            }
            return Ok(tableList);
        }
    }
}
