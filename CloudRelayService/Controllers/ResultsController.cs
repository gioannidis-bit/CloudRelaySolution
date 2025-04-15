using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using CloudRelayService.Models;
using System.Runtime.CompilerServices;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ResultsController : ControllerBase
    {
        private readonly ILogger<ResultsController> _logger;
        private readonly IConfiguration _configuration;
        private readonly HubConnection _connection;

        public ResultsController(ILogger<ResultsController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Initialize the hub connection
            string hubUrl = _configuration["AgentHub:Url"] ?? "https://localhost:7197/hubs/agent";
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Start the connection asynchronously
            _connection.StartAsync().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _logger.LogError(task.Exception, "Failed to connect to agent hub");
                }
            });
        }

        /// <summary>
        /// Gets the status of a query
        /// </summary>
        /// <param name="agentId">The ID of the agent</param>
        /// <param name="queryId">The ID of the query</param>
        /// <returns>Query status</returns>
        [HttpGet("{agentId}/status/{queryId}")]
        public async Task<ActionResult<QueryStatus>> GetQueryStatus(string agentId, string queryId)
        {
            try
            {
                _logger.LogInformation($"Getting status for query {queryId} from agent {agentId}");

                if (_connection.State != HubConnectionState.Connected)
                {
                    await _connection.StartAsync();
                }

                var result = await _connection.InvokeAsync<QueryStatus>("GetQueryStatus", agentId, queryId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to get status for query {queryId} from agent {agentId}");
                return StatusCode(500, "Failed to get query status");
            }
        }

        /// <summary>
        /// Runs a query on an agent
        /// </summary>
        /// <param name="agentId">The ID of the agent</param>
        /// <param name="queryIndex">The index of the query</param>
        /// <param name="destinationId">Optional destination ID</param>
        /// <returns>Query result</returns>
        [HttpGet("{agentId}/run")]
        public async Task<ActionResult<QueryResult>> RunQuery(string agentId, [FromQuery] string queryIndex, [FromQuery] string? destinationId = null)
        {
            try
            {
                _logger.LogInformation($"Running query {queryIndex} on agent {agentId}");

                if (_connection.State != HubConnectionState.Connected)
                {
                    await _connection.StartAsync();
                }

                var queryRequest = new QueryRequest
                {
                    Id = Guid.NewGuid().ToString(),
                    AgentId = agentId,
                    QueryIndex = queryIndex,
                    DestinationId = destinationId
                };

                var result = await _connection.InvokeAsync<QueryResult>("ExecuteQuery", queryRequest);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to run query {queryIndex} on agent {agentId}");
                return StatusCode(500, "Failed to run query");
            }
        }

        /// <summary>
        /// Streams query results from an agent
        /// </summary>
        /// <param name="agentId">The ID of the agent</param>
        /// <param name="queryIndex">The index of the query</param>
        /// <param name="destinationId">Optional destination ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Stream of query data</returns>
        [HttpGet("{agentId}/runstream")]
        public async IAsyncEnumerable<QueryDataChunk> RunQueryStream(
            string agentId,
            [FromQuery] string queryIndex,
            [FromQuery] string? destinationId = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"Streaming query {queryIndex} from agent {agentId}");

            // Ensure connection before streaming
            if (_connection.State != HubConnectionState.Connected)
            {
                try
                {
                    await _connection.StartAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to connect to hub for streaming query {queryIndex} from agent {agentId}");
                    yield break; // Stop enumeration if connection fails
                }
            }

            var queryId = Guid.NewGuid().ToString();

            // Create the stream outside the try block
            IAsyncEnumerable<QueryDataChunk> stream;

            try
            {
                // Use the correct streaming extension method
                stream = _connection.StreamAsync<QueryDataChunk>(
                    "StreamLargeQueryData",
                    queryId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error initializing stream for query {queryIndex} from agent {agentId}");
                yield break; // Stop enumeration if stream initialization fails
            }

            // Now use the stream outside the try-catch
            await foreach (var chunk in stream.WithCancellation(cancellationToken))
            {
                yield return chunk;
            }
        }
    }
}