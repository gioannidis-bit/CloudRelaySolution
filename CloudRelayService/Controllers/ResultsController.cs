using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudRelayService.Hubs; // Make sure this namespace matches your project
using Newtonsoft.Json;
using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ResultsController : ControllerBase
    {
        private readonly IHubContext<AgentHub> _hubContext;
        private const int PageSize = 500; // Adjust chunk size if needed

        public ResultsController(IHubContext<AgentHub> hubContext)
        {
            _hubContext = hubContext;
        }

        /// <summary>
        /// Streaming endpoint that calls the agent to run a query (based on queryIndex) and returns data in chunks.
        /// If destinationId is provided, performs bulk insert of the chunks into the destination server.
        /// 
        /// Call example:
        /// GET https://192.168.14.121:7197/api/results/{agentId}/runstream?queryIndex=3&destinationId=YOUR_DESTINATION_ID
        /// </summary>
        [HttpGet("{agentId}/runstream")]
        public async IAsyncEnumerable<string> RunQueryStream(
     string agentId,
     [FromQuery] string queryIndex,
     [FromQuery] string? destinationId,
     [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Check if agent exists
            if (!AgentHub.Agents.TryGetValue(agentId, out var agent))
            {
                yield return $"Error: Agent {agentId} not found or not connected";
                yield break;
            }

            yield return $"Starting data stream from agent {agentId}...";

            // Create a channel for data
            var channel = Channel.CreateUnbounded<string>();

            // Register the channel for this agent
            if (AgentHub.QueryChannels.TryRemove(agentId, out _))
            {
                Console.WriteLine($"Replacing existing channel for agent {agentId}");
            }

            AgentHub.QueryChannels[agentId] = channel;
            Console.WriteLine($"Channel created for agent {agentId}");

            // Tell the agent to start streaming data
            bool sendError = false;
            string errorMessage = "";

            try
            {
                Console.WriteLine($"Sending GetStreamData to agent {agentId}");
                await _hubContext.Clients.Group(agentId).SendAsync("GetStreamData", queryIndex, cancellationToken);
                Console.WriteLine($"GetStreamData sent to agent {agentId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending GetStreamData to agent {agentId}: {ex.Message}");
                errorMessage = ex.Message;
                sendError = true;
            }

            if (sendError)
            {
                AgentHub.QueryChannels.TryRemove(agentId, out _);
                yield return $"Error starting data stream: {errorMessage}";
                yield break;
            }

            // Read from the channel using the helper method
            var reader = channel.Reader;
            string dataChunk;

            while ((dataChunk = await GetNextChunkSafely(reader, cancellationToken)) != null)
            {
                yield return dataChunk;
            }

            // Clean up
            AgentHub.QueryChannels.TryRemove(agentId, out _);
            Console.WriteLine($"Channel for agent {agentId} removed");

            yield return "Data stream completed";
        }

        // Helper method to safely get the next chunk
        private async Task<string> GetNextChunkSafely(ChannelReader<string> reader, CancellationToken token)
        {
            try
            {
                // Wait for data with a timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                if (!await reader.WaitToReadAsync(linkedCts.Token))
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

        private (string, string)? ExtractSchemaAndTableName(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;
            query = query.Trim();
            int fromIndex = query.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
            if (fromIndex == -1)
                return null;
            string afterFrom = query.Substring(fromIndex + 4).Trim();
            int spaceIndex = afterFrom.IndexOf(" ");
            if (spaceIndex > 0)
                afterFrom = afterFrom.Substring(0, spaceIndex);
            string[] parts = afterFrom.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim().Trim('[', ']');
            }
            if (parts.Length == 0)
                return null;
            if (parts.Length == 1)
                return ("dbo", parts[0]);
            if (parts.Length == 2)
                return (parts[0], parts[1]);
            return (parts[1], parts[parts.Length - 1]);
        }

        private string BuildCreateTableDDL(DataTable dt, string schema, string tableName)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"CREATE TABLE [{schema}].[{tableName}] (");
            foreach (DataColumn col in dt.Columns)
            {
                string sqlType = MapType(col.DataType);
                sb.AppendLine($"    [{col.ColumnName}] {sqlType} NULL,");
            }
            if (dt.Columns.Count > 0)
            {
                sb.Length -= 3;
                sb.AppendLine();
            }
            sb.Append(");");
            return sb.ToString();
        }

        private string MapType(Type type)
        {
            if (type == typeof(string))
                return "nvarchar(max)";
            if (type == typeof(int))
                return "int";
            if (type == typeof(long))
                return "bigint";
            if (type == typeof(decimal))
                return "decimal(18,2)";
            if (type == typeof(double))
                return "float";
            if (type == typeof(DateTime))
                return "datetime";
            if (type == typeof(bool))
                return "bit";
            return "nvarchar(max)";
        }

        private async Task BulkInsertWithPaging(SqlConnection sqlConn, DataTable dt, string fullTableName, int pageSize)
        {
            int totalRows = dt.Rows.Count;
            for (int offset = 0; offset < totalRows; offset += pageSize)
            {
                DataTable dtPage = dt.Clone();
                int rowsToCopy = Math.Min(pageSize, totalRows - offset);
                for (int i = 0; i < rowsToCopy; i++)
                {
                    dtPage.ImportRow(dt.Rows[offset + i]);
                }
                using (var bulkCopy = new SqlBulkCopy(sqlConn))
                {
                    bulkCopy.DestinationTableName = fullTableName;
                    bulkCopy.BulkCopyTimeout = 300; // Timeout in seconds
                    await bulkCopy.WriteToServerAsync(dtPage);
                }
            }
        }
    }
}
