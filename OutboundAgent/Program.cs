using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using AgentClientService; // Use the namespace where Worker resides


IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService() // Enable running as a Windows Service.
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();





namespace AgentClient
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

    class Program
    {
        static AgentConfiguration currentConfig = new AgentConfiguration();
        static string agentPrimaryName = Environment.MachineName;
        static string agentCustomName = string.Empty;
        private const string AgentIdFileName = "agentid.txt";

        static async Task Main(string[] args)
        {
            Console.WriteLine("Agent starting...");

            var agentId = LoadOrCreateAgentId(AgentIdFileName);
            Console.WriteLine("Agent ID: " + agentId);
            Console.WriteLine("Primary Agent Name (Computer Name): " + agentPrimaryName);

            var connection = new HubConnectionBuilder()
                .WithUrl("https://195.46.18.174:7197/agentHub", options =>
                {                 
                    options.HttpMessageHandlerFactory = (handler) =>
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

            // GetData handler: uses the queryIndex parameter (as string).
            connection.On("GetData", async (string queryIndexParam) =>
            {
                Console.WriteLine("Received GetData command from server. Query index parameter: " + queryIndexParam);
                string jsonData;
                if (!string.IsNullOrWhiteSpace(queryIndexParam) && int.TryParse(queryIndexParam, out int index))
                {
                    jsonData = await ExecuteQueryAtIndex(index);
                }
                else
                {
                    jsonData = await ExecuteQueriesForAllConnections();
                }
                await connection.InvokeAsync("SendData", agentId, jsonData);
                Console.WriteLine("Sent query data to server.");
            });

            // TestConnection handler.
            connection.On("TestConnection", async () =>
            {
                Console.WriteLine("Received TestConnection command from server.");
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
                await connection.InvokeAsync("TestConnectionResult", agentId, testResult);
                Console.WriteLine("Sent test connection result to server: " + testResult);
            });

            connection.Reconnecting += error =>
            {
                Console.WriteLine("Connection lost. Reconnecting now...");
                return Task.CompletedTask;
            };

            connection.Reconnected += async connectionId =>
            {
                Console.WriteLine("Reconnected with new connection ID: " + connectionId);
                try
                {
                    await connection.InvokeAsync("RegisterAgent", agentId, agentPrimaryName, agentCustomName);
                    Console.WriteLine("Re-registered agent after reconnection.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error re-registering agent: " + ex.Message);
                }
            };

            connection.Closed += async error =>
            {
                Console.WriteLine("Connection closed. Error: " + error?.Message);
                while (true)
                {
                    try
                    {
                        Console.WriteLine("Attempting manual reconnection...");
                        await connection.StartAsync();
                        Console.WriteLine("Manual reconnection succeeded.");
                        await connection.InvokeAsync("RegisterAgent", agentId, agentPrimaryName, agentCustomName);
                        Console.WriteLine("Agent re-registered after manual reconnection.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Manual reconnection failed: " + ex.Message);
                        await Task.Delay(2000);
                    }
                }
            };

            connection.On<AgentConfiguration>("UpdateConfig", (config) =>
            {
                Console.WriteLine("Received configuration update.");
                currentConfig = config;
                Console.WriteLine("Configuration updated. Connections count: " + currentConfig.Connections.Count);
                if (!string.IsNullOrWhiteSpace(config.CustomAgentName))
                {
                    agentCustomName = config.CustomAgentName;
                    Console.WriteLine("Updated custom friendly name: " + agentCustomName);
                }
            });

            bool isConnected = false;
            while (!isConnected)
            {
                try
                {
                    await connection.StartAsync();
                    isConnected = true;
                    Console.WriteLine("Connected to the CloudRelayService.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Server not available: " + ex.Message + " Retrying in 2 seconds...");
                    await Task.Delay(2000);
                }
            }

            try
            {
                await connection.InvokeAsync("RegisterAgent", agentId, agentPrimaryName, agentCustomName);
                Console.WriteLine("Agent registered with the server.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error registering agent: " + ex.Message);
            }

            await Task.Delay(-1);
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

        static async Task<string> ExecuteQueriesForAllConnections()
        {
            // This list will accumulate one entry per executed query.
            var resultList = new List<object>();

            // Loop through all connection configurations.
            foreach (var connConfig in currentConfig.Connections)
            {
                // Loop through all queries in this connection.
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
                                    // Deserialize serialized DataTable JSON to an object so that the final JSON is a proper array.
                                    var dataObject = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(dt));
                                    resultList.Add(dataObject);
                                    command.CommandTimeout = 300; // για 300 δευτερόλεπτα

                                }
                            }
                        }
                    }

                    catch (Exception ex)
                    {
                        // In case of error, add the error message instead.
                        resultList.Add("Error: " + ex.Message);
                    }
                }
            }
            return JsonConvert.SerializeObject(resultList);
        }



        static async Task<string> ExecuteQueryAtIndex(int index)
        {
            var resultList = new List<object>();
            if (currentConfig.Connections.Any())
            {
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
            }
            else
            {
                resultList.Add("No connection configuration available.");
            }
            return JsonConvert.SerializeObject(resultList);
        }


    }
}
