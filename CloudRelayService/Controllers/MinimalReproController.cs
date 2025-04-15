using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MinimalReproController : ControllerBase
    {
        private readonly ILogger<MinimalReproController> _logger;

        public MinimalReproController(ILogger<MinimalReproController> logger)
        {
            _logger = logger;
        }

        // GET /minimalrepro/test
        [HttpGet("test")]
        public IActionResult TestSerialization()
        {
            // Create a minimal test payload.
            var payload = new
            {
                
                Content = "",
                Timestamp = DateTime.MinValue  // "0001-01-01T00:00:00" in ISO 8601
            };

            try
            {
                // Attempt to serialize the payload.
                string jsonResult = JsonSerializer.Serialize(payload);
                _logger.LogInformation("Serialization successful: {json}", jsonResult);
                return Ok(new { success = true, json = jsonResult });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Serialization error: {message}", ex.Message);
                return BadRequest("Serialization error: " + ex.Message);
            }
        }
    }
}
