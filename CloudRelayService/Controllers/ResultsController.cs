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
using Microsoft.AspNetCore.SignalR.Client;
using System.Runtime.CompilerServices;

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
            // List to collect errors and informational messages
            var outputChunks = new System.Collections.Generic.List<string>();

            // Create a temporary SignalR connection to the AgentHub.
            var hubConnection = new HubConnectionBuilder()
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
                .WithAutomaticReconnect()
                .Build();

            try
            {
                await hubConnection.StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                outputChunks.Add($"Error starting connection: {ex.Message}");
            }
            if (outputChunks.Count > 0)
            {
                foreach (var msg in outputChunks)
                    yield return msg;
                yield break;
            }

            // Call the streaming method on the agent.
            IAsyncEnumerable<string> stream = null;
            try
            {
                stream = hubConnection.StreamAsync<string>("StreamLargeQueryData", agentId, queryIndex, cancellationToken);
            }
            catch (Exception ex)
            {
                outputChunks.Add($"Error calling streaming method: {ex.Message}");
            }
            if (outputChunks.Count > 0)
            {
                foreach (var msg in outputChunks)
                    yield return msg;
                yield break;
            }

            // If destinationId is provided, prepare for bulk insert.
            bool useDestination = !string.IsNullOrWhiteSpace(destinationId);
            SqlConnection? destConn = null;
            bool tableCreated = false;
            string? schema = null, tableName = null, fullTableName = null;
            if (useDestination)
            {
                var destConfig = DestinationStore.Destinations.Find(d => d.Id == destinationId);
                if (destConfig == null)
                {
                    outputChunks.Add("Error: Destination configuration not found.");
                }
                else
                {
                    var builder = new SqlConnectionStringBuilder(destConfig.ConnectionString)
                    {
                        TrustServerCertificate = true // Always use trust true
                    };
                    string destConnStr = builder.ConnectionString;
                    destConn = new SqlConnection(destConnStr);
                    try
                    {
                        await destConn.OpenAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        outputChunks.Add($"Error opening destination connection: {ex.Message}");
                    }
                }
            }
            if (outputChunks.Count > 0)
            {
                foreach (var msg in outputChunks)
                    yield return msg;
                yield break;
            }

            // Process each chunk returned by the agent.
            await foreach (var chunk in stream.WithCancellation(cancellationToken))
            {
                yield return chunk; // Return the chunk to the client

                if (useDestination && destConn != null)
                {
                    DataTable dtChunk;
                    try
                    {
                        dtChunk = JsonConvert.DeserializeObject<DataTable>(chunk);
                        if (dtChunk == null || dtChunk.Columns.Count == 0)
                        {
                            outputChunks.Add("Error: No data available in the received chunk.");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        outputChunks.Add($"Error deserializing chunk: {ex.Message}");
                        continue;
                    }

                    // On first chunk: Create the destination table.
                    if (!tableCreated)
                    {
                        (string, string)? extraction = null;
                        if (AgentHub.Agents.TryGetValue(agentId, out var agent))
                        {
                            if (agent.Configuration != null &&
                                agent.Configuration.Connections?.Count > 0 &&
                                int.TryParse(queryIndex, out int qIdx) &&
                                qIdx < agent.Configuration.Connections[0].Queries.Count)
                            {
                                string queryText = agent.Configuration.Connections[0].Queries[qIdx];
                                extraction = ExtractSchemaAndTableName(queryText);
                            }
                        }
                        if (extraction == null)
                        {
                            outputChunks.Add("Error: Could not extract schema and table name from the query.");
                        }
                        else
                        {
                            schema = extraction.Value.Item1;
                            tableName = extraction.Value.Item2;
                            fullTableName = $"[{schema}].[{tableName}]";

                            // Create the schema if it does not exist.
                            string createSchemaSql = $@"
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema}')
BEGIN
    EXEC('CREATE SCHEMA [{schema}]');
END";
                            try
                            {
                                using (var schemaCmd = new SqlCommand(createSchemaSql, destConn))
                                {
                                    schemaCmd.CommandTimeout = 300;
                                    await schemaCmd.ExecuteNonQueryAsync(cancellationToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                outputChunks.Add($"Error creating schema: {ex.Message}");
                            }

                            // Drop table if exists.
                            try
                            {
                                string dropTableSql = $@"IF OBJECT_ID('{fullTableName}', 'U') IS NOT NULL DROP TABLE {fullTableName};";
                                using (var dropCmd = new SqlCommand(dropTableSql, destConn))
                                {
                                    dropCmd.CommandTimeout = 300;
                                    await dropCmd.ExecuteNonQueryAsync(cancellationToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                outputChunks.Add($"Error dropping existing table: {ex.Message}");
                            }
                            // Create table.
                            try
                            {
                                string createTableDDL = BuildCreateTableDDL(dtChunk, schema, tableName);
                                using (var createCmd = new SqlCommand(createTableDDL, destConn))
                                {
                                    createCmd.CommandTimeout = 300;
                                    await createCmd.ExecuteNonQueryAsync(cancellationToken);
                                }
                                tableCreated = true;
                            }
                            catch (Exception ex)
                            {
                                outputChunks.Add($"Error creating table: {ex.Message}");
                            }
                        }
                        // If errors were accumulated during table creation, yield them.
                        if (outputChunks.Count > 0)
                        {
                            foreach (var msg in outputChunks)
                                yield return msg;
                        }
                    } // end table creation

                    // Bulk insert the chunk into the destination.
                    try
                    {
                        await BulkInsertWithPaging(destConn, dtChunk, fullTableName!, PageSize);
                    }
                    catch (Exception ex)
                    {
                        outputChunks.Add($"Error bulk inserting data: {ex.Message}");
                    }
                } // end if useDestination
            } // end foreach stream

            if (useDestination && destConn != null)
            {
                await destConn.CloseAsync();
                destConn.Dispose();
                outputChunks.Add("Data inserted successfully into destination database.");
            }

            // Yield any final messages
            foreach (var msg in outputChunks)
            {
                yield return msg;
            }

            await hubConnection.StopAsync(cancellationToken);
            await hubConnection.DisposeAsync();
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
