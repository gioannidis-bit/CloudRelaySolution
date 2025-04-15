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
using System.Threading.Tasks;

namespace CloudRelayService.Hubs
{
    public class AgentHub : Hub
    {
        // In-memory store for connected agents.
        public static ConcurrentDictionary<string, AgentInfo> Agents = new ConcurrentDictionary<string, AgentInfo>();

        // Dictionary to store pending query requests (non-streaming).
        public static ConcurrentDictionary<string, TaskCompletionSource<string>> QueryCompletionSources
            = new ConcurrentDictionary<string, TaskCompletionSource<string>>();

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
            await Clients.All.SendAsync("AgentStatusChanged", info);

            if (existingConfig != null)
            {
                await Clients.Client(Context.ConnectionId).SendAsync("UpdateConfig", existingConfig);
            }
        }

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

        public async Task SendDataChunk(string agentId, string chunk)
        {
            // Forward the chunk to all connected clients.
            await Clients.All.SendAsync("AgentDataChunk", agentId, chunk);
        }

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
        public string Id { get; set; }
        public string ConnectionString { get; set; }
        public List<string> Queries { get; set; } = new List<string>();
    }
}
