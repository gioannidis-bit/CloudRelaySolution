using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AgentClientService
{
    public class AgentConfiguration
    {
        public List<ConnectionConfig> Connections { get; set; } = new List<ConnectionConfig>();
        public string CustomAgentName { get; set; }
    }

    public class ConnectionConfig
    {
        public string Id { get; set; }
        public string ConnectionString { get; set; }
        public List<string> Queries { get; set; } = new List<string>();
    }

    public class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => TimeSpan.FromSeconds(2);
    }

    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private HubConnection _connection;
        private AgentConfiguration currentConfig = new AgentConfiguration();
        private string agentPrimaryName = Environment.MachineName;
        private string agentCustomName = string.Empty;
        private const string AgentIdFileName = "agentid.txt";
        private string agentId;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            agentId = LoadOrCreateAgentId(AgentIdFileName);
            _logger.LogInformation("Agent starting with ID: {AgentId} | Primary Name: {PrimaryName}", agentId, agentPrimaryName);

            _connection = new HubConnectionBuilder()
                .WithUrl("https://192.168.14.121:7197/agentHub", options =>
                {
                    options.HttpMessageHandlerFactory = handler =>
                    {
                        if (handler is System.Net.Http.HttpClientHandler clientHandler)
                        {
                            clientHandler.ServerCertificateCustomValidationCallback =
                                System.Net.Http.HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                        }
                        return handler;
                    };
                })
                .WithAutomaticReconnect(new ForeverRetryPolicy())
                .Build();

            // Handler to process standard GetData commands from the server.
            _connection.On("GetData", async (string queryIndexParam) =>
            {
                _logger.LogInformation("Received GetData command. QueryIndexParam: {Param}", queryIndexParam);
                string jsonData;
                if (!string.IsNullOrWhiteSpace(queryIndexParam) && int.TryParse(queryIndexParam, out int index))
                {
                    jsonData = await ExecuteQueryAtIndex(index);
                }
                else
                {
                    jsonData = await ExecuteQueriesForAllConnections();
                }
                await _connection.InvokeAsync("SendData", agentId, jsonData);
                _logger.LogInformation("Sent query data to server.");
            });

            // Handler for TestConnection commands.
            _connection.On("TestConnection", async () =>
            {
                _logger.LogInformation("Received TestConnection command.");
                string testResult;
                if (currentConfig.Connections.Any())
                {
                    var connConfig = currentConfig.Connections.First();
                    try
                    {
                        using (var sqlConn = new SqlConnection(connConfig.ConnectionString))
                        {
                            await sqlConn.OpenAsync();
                            testResult = "Connection successful.";
                        }
                    }
                    catch (Exception ex)
                    {
                        testResult = "Connection error: " + ex.Message;
                    }
                }
                else
                {
                    testResult = "No connection string configured.";
                }
                await _connection.InvokeAsync("TestConnectionResult", agentId, testResult);
                _logger.LogInformation("Sent test connection result: {Result}", testResult);
            });

            // Handler to update local configuration.
            _connection.On<AgentConfiguration>("UpdateConfig", (config) =>
            {
                _logger.LogInformation("Received configuration update.");
                currentConfig = config;
                _logger.LogInformation("Configuration updated. Connections count: {Count}", currentConfig.Connections.Count);
                if (!string.IsNullOrWhiteSpace(config.CustomAgentName))
                {
                    agentCustomName = config.CustomAgentName;
                    _logger.LogInformation("Updated custom friendly name: {Name}", agentCustomName);
                }
            });

            // NEW: Handler for streaming query data. The server will call this with a queryIndex.
            // Update your GetStreamData handler in the Worker class
            // In your Worker class, update the "GetStreamData" handler:
            _connection.On("GetStreamData", async (string queryIndex) =>
            {
                _logger.LogInformation("Received GetStreamData command for queryIndex: {QueryIndex}", queryIndex);

                try
                {
                    // Send a start message
                    await _connection.SendAsync("SendDataChunk", agentId, "Starting query execution...", false);

                    // Parse the query index
                    if (!int.TryParse(queryIndex, out int qIndex))
                    {
                        _logger.LogError("Invalid query index: {QueryIndex}", queryIndex);
                        await _connection.SendAsync("SendDataChunk", agentId, $"Error: Invalid query index {queryIndex}");
                        await _connection.SendAsync("SendDataChunk", agentId, "Query execution failed", true);
                        return;
                    }

                    // Check if we have connections configured
                    if (currentConfig.Connections == null || !currentConfig.Connections.Any())
                    {
                        _logger.LogError("No connections configured");
                        await _connection.SendAsync("SendDataChunk", agentId, "Error: No connections configured");
                        await _connection.SendAsync("SendDataChunk", agentId, "Query execution failed", true);
                        return;
                    }

                    // Get the connection and query
                    var connConfig = currentConfig.Connections.First();
                    if (qIndex < 0 || qIndex >= connConfig.Queries.Count)
                    {
                        _logger.LogError("Query index out of range: {QueryIndex}", qIndex);
                        await _connection.SendAsync("SendDataChunk", agentId, $"Error: Query index {qIndex} out of range");
                        await _connection.SendAsync("SendDataChunk", agentId, "Query execution failed", true);
                        return;
                    }

                    string query = connConfig.Queries[qIndex];
                    _logger.LogInformation("Executing SQL query: {Query}", query);

                    // Execute the SQL query and get the data
                    using (var sqlConn = new SqlConnection(connConfig.ConnectionString))
                    {
                        await sqlConn.OpenAsync();
                        using (var command = new SqlCommand(query, sqlConn))
                        {
                            command.CommandTimeout = 300; // 5 minutes
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                var dt = new DataTable();
                                dt.Load(reader);

                                // Send a diagnostic message about the data
                                _logger.LogInformation("Query returned {RowCount} rows", dt.Rows.Count);

                                // Convert to JSON and send
                                string jsonData = JsonConvert.SerializeObject(dt);
                                _logger.LogInformation("Serialized data size: {Size} bytes", jsonData.Length);

                                // Send the JSON data (this is the actual query result)
                                await _connection.SendAsync("SendDataChunk", agentId, jsonData, false);
                            }
                        }
                    }

                    // Final message
                    await _connection.SendAsync("SendDataChunk", agentId, "Query execution completed", true);
                    _logger.LogInformation("Query executed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing query");
                    await _connection.SendAsync("SendDataChunk", agentId, $"Error: {ex.Message}");
                    await _connection.SendAsync("SendDataChunk", agentId, "Query execution failed", true);
                }
            });

            _connection.Reconnecting += error =>
            {
                _logger.LogWarning("Connection lost. Reconnecting...");
                return Task.CompletedTask;
            };

            _connection.Reconnected += async connectionId =>
            {
                _logger.LogInformation("Reconnected with new connection ID: {ConnectionId}", connectionId);
                try
                {
                    await _connection.InvokeAsync("RegisterAgent", agentId, agentPrimaryName, agentCustomName);
                    _logger.LogInformation("Re-registered agent after reconnection.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error re-registering agent.");
                }
            };

            _connection.Closed += async error =>
            {
                _logger.LogError("Connection closed: {Message}", error?.Message);
                while (true)
                {
                    try
                    {
                        _logger.LogInformation("Attempting manual reconnection...");
                        await _connection.StartAsync();
                        _logger.LogInformation("Manual reconnection succeeded.");
                        await _connection.InvokeAsync("RegisterAgent", agentId, agentPrimaryName, agentCustomName);
                        _logger.LogInformation("Agent re-registered after manual reconnection.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Manual reconnection failed.");
                        await Task.Delay(2000);
                    }
                }
            };

            bool isConnected = false;
            while (!isConnected && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _connection.StartAsync(cancellationToken);
                    isConnected = true;
                    _logger.LogInformation("Connected to CloudRelayService.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Server not available. Retrying in 2 seconds...");
                    await Task.Delay(2000, cancellationToken);
                }
            }

            try
            {
                await _connection.InvokeAsync("RegisterAgent", agentId, agentPrimaryName, agentCustomName, cancellationToken);
                _logger.LogInformation("Agent registered with the server.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering agent.");
            }

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        private static string LoadOrCreateAgentId(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var existingId = File.ReadAllText(filePath).Trim();
                    if (!string.IsNullOrEmpty(existingId))
                        return existingId;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading agent ID file: " + ex.Message);
            }
            var newId = Guid.NewGuid().ToString();
            try
            {
                File.WriteAllText(filePath, newId);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing agent ID to file: " + ex.Message);
            }
            return newId;
        }

        // This method streams query results in chunks.
        // All error or informational messages are collected in a local list (outputs)
        // and yielded outside the try/catch blocks to avoid "yield return" in catch clauses.
        private async IAsyncEnumerable<string> StreamQueryData(string queryIndex, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var outputs = new List<string>();

            string sqlQuery;
            if (currentConfig.Connections.Any() &&
                int.TryParse(queryIndex, out int qIndex) &&
                qIndex < currentConfig.Connections[0].Queries.Count)
            {
                sqlQuery = currentConfig.Connections[0].Queries[qIndex];
            }
            else
            {
                sqlQuery = "SELECT TOP 300000 * FROM YourLargeTable ORDER BY Id";
            }

            string connectionString = currentConfig.Connections[0].ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                outputs.Add("Error: Connection string not configured.");
                foreach (var message in outputs)
                    yield return message;
                yield break;
            }

            int chunkSize = 500;
            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync(cancellationToken);
                    using (var cmd = new SqlCommand(sqlQuery, conn))
                    {
                        cmd.CommandTimeout = 300;
                        using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                        {
                            DataTable dtChunk = CreateEmptyTableFromReader(reader);
                            int currentRowCount = 0;
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                DataRow row = dtChunk.NewRow();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                                }
                                dtChunk.Rows.Add(row);
                                currentRowCount++;
                                if (currentRowCount >= chunkSize)
                                {
                                    string jsonChunk = JsonConvert.SerializeObject(dtChunk);
                                    outputs.Add(jsonChunk);
                                    dtChunk.Clear();
                                    currentRowCount = 0;
                                }
                            }
                            if (dtChunk.Rows.Count > 0)
                            {
                                string jsonChunk = JsonConvert.SerializeObject(dtChunk);
                                outputs.Add(jsonChunk);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                outputs.Add("Error streaming data: " + ex.Message);
            }

            foreach (var msg in outputs)
            {
                yield return msg;
            }
        }

        private DataTable CreateEmptyTableFromReader(SqlDataReader reader)
        {
            DataTable table = new DataTable();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                DataColumn column = new DataColumn(reader.GetName(i), reader.GetFieldType(i));
                table.Columns.Add(column);
            }
            return table;
        }

        // Existing non-streaming methods (ExecuteQueriesForAllConnections and ExecuteQueryAtIndex) remain unchanged.
        private async Task<string> ExecuteQueriesForAllConnections()
        {
            var resultList = new List<object>();

            if (currentConfig.Connections == null || !currentConfig.Connections.Any())
            {
                resultList.Add("No connection configuration available.");
                return JsonConvert.SerializeObject(resultList);
            }

            foreach (var connConfig in currentConfig.Connections)
            {
                foreach (var query in connConfig.Queries)
                {
                    try
                    {
                        using (var sqlConn = new SqlConnection(connConfig.ConnectionString))
                        {
                            await sqlConn.OpenAsync();
                            using (var command = new SqlCommand(query, sqlConn))
                            {
                                using (var reader = await command.ExecuteReaderAsync())
                                {
                                    var dt = new DataTable();
                                    dt.Load(reader);
                                    var dataObject = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(dt));
                                    resultList.Add(dataObject);
                                    command.CommandTimeout = 300;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        resultList.Add("Error: " + ex.Message);
                    }
                }
            }
            return JsonConvert.SerializeObject(resultList);
        }

        private async Task<string> ExecuteQueryAtIndex(int index)
        {
            var resultList = new List<object>();
            if (currentConfig.Connections == null || !currentConfig.Connections.Any())
            {
                resultList.Add("No connection configuration available.");
                return JsonConvert.SerializeObject(resultList);
            }

            var connConfig = currentConfig.Connections.First();
            if (index >= 0 && index < connConfig.Queries.Count)
            {
                string query = connConfig.Queries[index];
                try
                {
                    using (var sqlConn = new SqlConnection(connConfig.ConnectionString))
                    {
                        await sqlConn.OpenAsync();
                        using (var command = new SqlCommand(query, sqlConn))
                        {
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                var dt = new DataTable();
                                dt.Load(reader);
                                var dataObject = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(dt));
                                resultList.Add(dataObject);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    resultList.Add("Error: " + ex.Message);
                }
            }
            else
            {
                resultList.Add("Invalid query index.");
            }
            return JsonConvert.SerializeObject(resultList);
        }
    }
}
