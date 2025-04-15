using CloudRelayService.Models;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;

namespace CloudRelayService.Hubs
{
    public class AgentHub : Hub
    {
        private readonly ILogger<AgentHub> _logger;

        public AgentHub(ILogger<AgentHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Adds a new agent to the hub
        /// </summary>
        /// <param name="agentId"></param>
        /// <returns></returns>
        public async Task RegisterAgent(string agentId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, agentId);
            _logger.LogInformation($"Agent {agentId} connected with connection id {Context.ConnectionId}");
        }

        /// <summary>
        /// Removes an agent from the hub
        /// </summary>
        /// <param name="agentId"></param>
        /// <returns></returns>
        public async Task UnregisterAgent(string agentId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, agentId);
            _logger.LogInformation($"Agent {agentId} disconnected");
        }

        /// <summary>
        /// Gets the status for a query
        /// </summary>
        /// <param name="agentId"></param>
        /// <param name="queryId"></param>
        /// <returns></returns>
        public async Task<QueryStatus> GetQueryStatus(string agentId, string queryId)
        {
            _logger.LogInformation($"Getting status for query {queryId} from agent {agentId}");
            return await Task.FromResult(new QueryStatus
            {
                Id = queryId,
                Status = "Unknown"
            });
        }

        /// <summary>
        /// Executes a query on an agent
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public async Task<QueryResult> ExecuteQuery(QueryRequest query)
        {
            _logger.LogInformation($"Executing query {query.Id} on agent {query.AgentId}");

            // Forward the query to the agent
            await Clients.Group(query.AgentId).SendAsync("QueryReceived", query);

            // Return a placeholder result
            return await Task.FromResult(new QueryResult
            {
                Id = query.Id,
                Result = "Query received"
            });
        }

        /// <summary>
        /// Streams large query data
        /// </summary>
        /// <param name="queryId">The ID of the query</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Stream of query data</returns>
        public async IAsyncEnumerable<QueryDataChunk> StreamLargeQueryData(
            string queryId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"Streaming data for query {queryId}");

            // Placeholder implementation - replace with your actual data streaming logic
            for (int i = 0; i < 10; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return new QueryDataChunk
                {
                    QueryId = queryId,
                    ChunkId = i.ToString(),
                    Data = $"Data chunk {i} for query {queryId}",
                    IsLastChunk = i == 9
                };

                await Task.Delay(100, cancellationToken);
            }
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }
    }
}