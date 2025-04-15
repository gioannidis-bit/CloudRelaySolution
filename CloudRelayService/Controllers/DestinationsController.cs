using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DestinationsController : ControllerBase
    {
        // GET: api/destinations
        [HttpGet]
        public IActionResult GetDestinations()
        {
            return Ok(DestinationStore.Destinations);
        }

        // POST: api/destinations
        [HttpPost]
        public IActionResult AddDestination([FromBody] DestinationConfig config)
        {
            if (string.IsNullOrEmpty(config.Id))
            {
                config.Id = Guid.NewGuid().ToString();
            }
            DestinationStore.Destinations.Add(config);
            DestinationStore.SaveDestinations();
            return Ok(config);
        }

        // PUT: api/destinations/{id}
        [HttpPut("{id}")]
        public IActionResult UpdateDestination(string id, [FromBody] DestinationConfig config)
        {
            var destination = DestinationStore.Destinations.FirstOrDefault(d => d.Id == id);
            if (destination == null)
                return NotFound("Destination not found");

            destination.ConnectionString = config.ConnectionString;
            destination.TableName = config.TableName;
            DestinationStore.SaveDestinations();
            return Ok(destination);
        }

        // DELETE: api/destinations/{id}
        [HttpDelete("{id}")]
        public IActionResult DeleteDestination(string id)
        {
            var destination = DestinationStore.Destinations.FirstOrDefault(d => d.Id == id);
            if (destination == null)
                return NotFound("Destination not found");

            DestinationStore.Destinations.Remove(destination);
            DestinationStore.SaveDestinations();
            return Ok("Deleted successfully");
        }
    }

    public class DestinationConfig
    {
        public string? Id { get; set; }  // Nullable because new entries may not have an ID yet.
        public string ConnectionString { get; set; }
        public string? TableName { get; set; }   // Optional
    }
}
