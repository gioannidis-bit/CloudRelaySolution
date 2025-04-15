using Microsoft.AspNetCore.SignalR.Client;
using OutboundAgent.Models;
using System.IO;
using System.Runtime.CompilerServices;

namespace OutboundAgent
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private HubConnection _connection;
        private long fileSize;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker starting at: {time}", DateTimeOffset.Now);
            await base.StartAsync(cancellationToken);
        }

        private async Task InitializeConnection(CancellationToken stoppingToken)
        {
            string hubUrl = _configuration["AgentHub:Url"] ?? throw new InvalidOperationException("Hub URL not configured");
            string agentId = _configuration["Agent:Id"] ?? Guid.NewGuid().ToString();

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Handle requests coming from the hub
            _connection.On<QueryRequest>("QueryReceived", async (query) =>
            {
                _logger.LogInformation($"Received query {query.Id} for agent {query.AgentId}");

                // Process the query based on its index
                if (query.QueryIndex == "1")
                {
                    await ProcessFileQuery(query);
                }
                else
                {
                    _logger.LogWarning($"Unknown query index: {query.QueryIndex}");
                }
            });

            try
            {
                await _connection.StartAsync(stoppingToken);
                _logger.LogInformation("Connected to hub");

                // Register the agent with the hub
                await _connection.InvokeAsync("RegisterAgent", agentId, stoppingToken);
                _logger.LogInformation($"Registered agent with ID: {agentId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to hub");
            }
        }

        private async Task ProcessFileQuery(QueryRequest query)
        {
            _logger.LogInformation($"Processing file query {query.Id}");

            try
            {
                string filePath = _configuration["DataFile:Path"] ?? throw new InvalidOperationException("Data file path not configured");

                if (!File.Exists(filePath))
                {
                    _logger.LogError($"File not found: {filePath}");
                    return;
                }

                fileSize = new FileInfo(filePath).Length;
                _logger.LogInformation($"File size: {fileSize} bytes");

                // Send the file in chunks
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (BufferedStream bufferedStream = new BufferedStream(fileStream))
                {
                    const int chunkSize = 4096;
                    byte[] buffer = new byte[chunkSize];
                    int bytesRead;
                    long totalBytesRead = 0;
                    int chunkIndex = 0;

                    while ((bytesRead = await bufferedStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        totalBytesRead += bytesRead;
                        bool isLastChunk = totalBytesRead >= fileSize;

                        // Create a data chunk
                        var dataChunk = new QueryDataChunk
                        {
                            QueryId = query.Id,
                            ChunkId = chunkIndex.ToString(),
                            // Convert the actual bytes read to Base64 string
                            Data = Convert.ToBase64String(buffer, 0, bytesRead),
                            IsLastChunk = isLastChunk
                        };

                        // Send the chunk to the destination if specified, otherwise process it locally
                        if (!string.IsNullOrEmpty(query.DestinationId))
                        {
                            // This will need to be implemented based on how you want to handle destinations
                            _logger.LogInformation($"Sending chunk {chunkIndex} to destination {query.DestinationId}");
                        }
                        else
                        {
                            _logger.LogInformation($"Processing chunk {chunkIndex} locally");
                        }

                        chunkIndex++;

                        if (isLastChunk)
                        {
                            _logger.LogInformation("File transfer complete");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing file query {query.Id}");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Initialize connection
            await InitializeConnection(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Reconnect if disconnected
                if (_connection.State == HubConnectionState.Disconnected)
                {
                    try
                    {
                        _logger.LogInformation("Reconnecting to hub...");
                        await _connection.StartAsync(stoppingToken);
                        _logger.LogInformation("Reconnected to hub");

                        // Re-register agent if needed
                        string agentId = _configuration["Agent:Id"] ?? Guid.NewGuid().ToString();
                        await _connection.InvokeAsync("RegisterAgent", agentId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to reconnect to hub");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // Unregister agent when stopping
            if (_connection != null && _connection.State == HubConnectionState.Connected)
            {
                string agentId = _configuration["Agent:Id"] ?? "";
                await _connection.InvokeAsync("UnregisterAgent", agentId);
                await _connection.StopAsync(cancellationToken);
                _logger.LogInformation("Disconnected from hub");
            }

            _logger.LogInformation("Worker stopping at: {time}", DateTimeOffset.Now);
            await base.StopAsync(cancellationToken);
        }
    }
}