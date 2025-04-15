using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CloudRelayService.Hubs
{
    public class AgentHub : Hub
    {
        // In-memory store for connected agents.
        public static ConcurrentDictionary<string, AgentInfo> Agents = new ConcurrentDictionary<string, AgentInfo>();

        // For non-streaming requests.
        public static ConcurrentDictionary<string, TaskCompletionSource<string>> QueryCompletionSources
            = new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        // NEW: Channels to hold streaming query chunks, keyed by agentId.
        public static ConcurrentDictionary<string, Channel<string>> QueryChannels = new ConcurrentDictionary<string, Channel<string>>();

        // Called by the agent to register.
        public async Task RegisterAgent(string agentId, string primaryName, string customName)
        {
            AgentConfiguration existingConfig = null;
            AgentConfigurationStore.Configurations.TryGetValue(agentId, out existingConfig);

            var info = new AgentInfo
            {
                AgentId = agentId,
                PrimaryName = primaryName,
                CustomName = (existingConfig != null && !string.IsNullOrWhiteSpace(existingConfig.CustomAgentName))
                                ? existingConfig.CustomAgentName
                                : customName,
                ConnectionId = Context.ConnectionId,
                IsOnline = true,
                LastConnected = DateTime.UtcNow,
                Configuration = existingConfig ?? new AgentConfiguration()
            };

            Agents[agentId] = info;

            // Add the connection to the agent's group
            await Groups.AddToGroupAsync(Context.ConnectionId, agentId);
            Console.WriteLine($"Added connection {Context.ConnectionId} to group {agentId}");

            await Clients.All.SendAsync("AgentStatusChanged", info);

            if (existingConfig != null)
            {
                await Clients.Client(Context.ConnectionId).SendAsync("UpdateConfig", existingConfig);
            }
        }

        // Called by the agent when it sends full (non-streaming) query results.
        public async Task SendData(string agentId, string jsonData)
        {
            if (Agents.TryGetValue(agentId, out var agent))
            {
                agent.LatestData = jsonData;
                await Clients.All.SendAsync("AgentDataUpdated", agentId, jsonData);
                if (QueryCompletionSources.TryRemove(agentId, out var tcs))
                {
                    tcs.SetResult(jsonData);
                }
            }
        }

        // Called by the agent to send individual streamed data chunks.
        // Called by the agent to send individual streamed data chunks.
        // Update your SendDataChunk method in AgentHub.cs
        // Called by the agent to send individual streamed data chunks.
        // Called by the agent to send individual streamed data chunks.
        // Called by the agent to send individual streamed data chunks.
     
        // Called by the agent to send individual streamed data chunks with last chunk flag.
        public async Task SendDataChunk(string agentId, string chunk, bool isLastChunk)
        {
            Console.WriteLine($"Received chunk from agent {agentId} (size: {chunk.Length} bytes)");

            // Write to the channel if registered
            if (QueryChannels.TryGetValue(agentId, out var channel))
            {
                try
                {
                    Console.WriteLine($"Writing chunk to channel for agent {agentId}");
                    await channel.Writer.WriteAsync(chunk);
                    Console.WriteLine($"Successfully wrote chunk to channel for agent {agentId}");

                    if (isLastChunk)
                    {
                        Console.WriteLine($"Last chunk received, completing channel for agent {agentId}");
                        channel.Writer.Complete();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing to channel for agent {agentId}: {ex.Message}");
                    if (isLastChunk)
                    {
                        try
                        {
                            channel.Writer.Complete(ex);
                        }
                        catch { }
                    }
                }
            }
            else
            {
                Console.WriteLine($"No channel found for agent {agentId}");
            }

            // Forward the chunk to any listening clients
            await Clients.All.SendAsync("AgentDataChunk", agentId, chunk);
        }

        // Add this method to your AgentHub class
        public async IAsyncEnumerable<string> StreamLargeQueryData(
    string agentId,
    string queryIndex,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"StreamLargeQueryData called for agent {agentId} with queryIndex {queryIndex}");

            // First message - outside any try-catch
            yield return $"Starting data stream from agent {agentId}...";

            // Create a channel for data
            Channel<string> channel;
            bool setupSuccessful = true;
            string setupError = "";

            // Setup phase - create channel and register it
            try
            {
                channel = Channel.CreateUnbounded<string>();

                if (QueryChannels.TryRemove(agentId, out _))
                {
                    Console.WriteLine($"Replacing existing channel for agent {agentId}");
                }

                QueryChannels[agentId] = channel;
                Console.WriteLine($"Channel created for agent {agentId}");

                // Tell the agent to start streaming data
                Console.WriteLine($"Sending GetStreamData to agent {agentId}");
                await Clients.Group(agentId).SendAsync("GetStreamData", queryIndex, cancellationToken);
                Console.WriteLine($"GetStreamData sent to agent {agentId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in setup: {ex.Message}");
                setupSuccessful = false;
                setupError = ex.Message;
            }

            // Check if setup was successful - outside try-catch
            if (!setupSuccessful)
            {
                yield return $"Error setting up data stream: {setupError}";
                yield break;
            }

            // Add a timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Reading phase - NO TRY-CATCH here at all
            var reader = QueryChannels[agentId].Reader;
            bool keepReading = true;
            string chunk = null;

            while (keepReading)
            {
                // Get the next chunk if available
                chunk = await GetNextChunkSafely(reader, linkedCts.Token);

                // If null, we're done
                if (chunk == null)
                {
                    keepReading = false;
                    continue;
                }

                // Return the chunk
                yield return chunk;
            }

            // Clean up - always do this
            QueryChannels.TryRemove(agentId, out _);
            Console.WriteLine($"Channel for agent {agentId} removed");

            // Final message - outside any try-catch
            yield return "Data stream completed";
        }

        // Helper method to safely get the next chunk without using yield in try-catch
        private async Task<string> GetNextChunkSafely(ChannelReader<string> reader, CancellationToken token)
        {
            try
            {
                // Check if there's more data
                if (!await reader.WaitToReadAsync(token))
                {
                    Console.WriteLine("Channel completed");
                    return null;
                }

                // Read the data
                if (reader.TryRead(out string chunk))
                {
                    Console.WriteLine($"Read chunk of size {chunk?.Length ?? 0}");
                    return chunk;
                }
                else
                {
                    Console.WriteLine("WaitToReadAsync returned true but TryRead failed");
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation timed out or was canceled");
                return "Operation timed out or was canceled";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading data: {ex.Message}");
                return $"Error reading data: {ex.Message}";
            }
        }

        // Called by the agent to send back test connection results.
        public async Task TestConnectionResult(string agentId, string result)
        {
            if (Agents.TryGetValue(agentId, out var agent))
            {
                agent.LatestData = result;
                await Clients.All.SendAsync("AgentTestConnectionResult", agentId, result);
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var agent = Agents.Values.FirstOrDefault(a => a.ConnectionId == Context.ConnectionId);
            if (agent != null)
            {
                agent.IsOnline = false;
                agent.LastDisconnected = DateTime.UtcNow;
                await Clients.All.SendAsync("AgentStatusChanged", agent);
            }
            await base.OnDisconnectedAsync(exception);
        }
    }

    public class AgentInfo
    {
        public string AgentId { get; set; }
        public string ConnectionId { get; set; }
        public string PrimaryName { get; set; }
        public string CustomName { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastConnected { get; set; }
        public DateTime? LastDisconnected { get; set; }
        public AgentConfiguration Configuration { get; set; }
        public string LatestData { get; set; }
    }

    public class AgentConfiguration
    {
        public List<ConnectionConfig> Connections { get; set; } = new List<ConnectionConfig>();
        public string CustomAgentName { get; set; }
    }

    public class ConnectionConfig
    {
        public string Id { get; set; }  // Typically a GUID.
        public string ConnectionString { get; set; }
        public List<string> Queries { get; set; } = new List<string>();
    }
}
