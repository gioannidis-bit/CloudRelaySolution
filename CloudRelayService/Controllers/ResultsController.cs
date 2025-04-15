using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudRelayService.Hubs;
using Newtonsoft.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Collections.Generic;

namespace CloudRelayService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ResultsController : ControllerBase
    {
        private readonly IHubContext<AgentHub> _hubContext;
        private const int PageSize = 500;

        public ResultsController(IHubContext<AgentHub> hubContext)
        {
            _hubContext = hubContext;
        }

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

            bool isBulkInsert = !string.IsNullOrWhiteSpace(destinationId);

            // If you prefer not to send control messages for non‐bulk mode, you can comment these out.
            if (!isBulkInsert)
            {
                // Optional: Remove or comment these out if you only want the data payload.
                // yield return $"Starting data stream from agent {agentId}...";
            }

            // Create and register channel
            var channel = Channel.CreateUnbounded<string>();
            if (AgentHub.QueryChannels.TryRemove(agentId, out _))
            {
                Console.WriteLine($"Replacing existing channel for agent {agentId}");
            }
            AgentHub.QueryChannels[agentId] = channel;
            Console.WriteLine($"Channel created for agent {agentId}");

            // Send command to agent
            bool commandSent = true;
            string errorMsg = "";
            try
            {
                await _hubContext.Clients.Group(agentId).SendAsync("GetStreamData", queryIndex, cancellationToken);
            }
            catch (Exception ex)
            {
                commandSent = false;
                errorMsg = ex.Message;
                Console.WriteLine($"Error sending command: {ex.Message}");
            }

            if (!commandSent)
            {
                AgentHub.QueryChannels.TryRemove(agentId, out _);
                if (!isBulkInsert)
                {
                    yield return $"Error starting data stream: {errorMsg}";
                }
                yield break;
            }

            // Variables for processing
            DataTable aggregatedDataTable = null;
            int totalRowsProcessed = 0;
            int batchesReceived = 0;
            bool hasError = false;
            string errorMessage = "";

            // Process messages from channel using asynchronous reading
            string chunk;
            while ((chunk = await ReadFromChannel(channel.Reader, cancellationToken)) != null)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                string processedMessage = "";
                string trimmedChunk = chunk.Trim();

                // Determine the message type based on the prefix
                if (IsControlMessage(trimmedChunk))
                {
                    // Optionally, you can choose not to yield control messages if you only want the result data.
                    if (isBulkInsert == false)
                    {
                        // For example, you may ignore these.
                        // processedMessage = chunk;
                    }
                }
                else if (trimmedChunk.StartsWith("SCHEMA:"))
                {
                    ProcessSchemaMessage(trimmedChunk, isBulkInsert, ref processedMessage);
                }
                else if (trimmedChunk.StartsWith("BATCH:"))
                {
                    ProcessBatchMessage(
                        trimmedChunk,
                        isBulkInsert,
                        ref aggregatedDataTable,
                        ref totalRowsProcessed,
                        ref batchesReceived,
                        ref processedMessage,
                        ref hasError,
                        ref errorMessage);
                }
                else if (trimmedChunk.StartsWith("SUMMARY:"))
                {
                    ProcessSummaryMessage(trimmedChunk, isBulkInsert, ref processedMessage);
                }
                else if (!isBulkInsert)
                {
                    processedMessage = chunk;
                }

                // Yield the processed message if it is non-empty.
                if (!string.IsNullOrEmpty(processedMessage))
                {
                    yield return processedMessage;
                }

                if (hasError)
                {
                    yield return $"Error processing data: {errorMessage}";
                    break;
                }
            }

            // Clean up the channel
            AgentHub.QueryChannels.TryRemove(agentId, out _);

            if (isBulkInsert)
            {
                // In bulk mode, continue with further processing for bulk insert
                if (aggregatedDataTable == null || aggregatedDataTable.Rows.Count == 0)
                {
                    yield return "No data received for bulk insert.";
                    yield break;
                }

                yield return $"Preparing to insert {totalRowsProcessed} rows...";

                var tableInfo = GetDestinationTableInfo(agent, queryIndex);
                if (!tableInfo.Valid)
                {
                    yield return tableInfo.ErrorMessage;
                    yield break;
                }

                string connStr = GetDestinationConnectionString(destinationId);
                if (string.IsNullOrEmpty(connStr))
                {
                    yield return $"Error: Invalid destination ID or connection string.";
                    yield break;
                }

                yield return $"Creating table {tableInfo.FullTableName}...";

                bool insertSuccess = true;
                string insertError = "";
                try
                {
                    // Pass the extracted table name to bulk insert (instead of dataTable.TableName)
                    await PerformBulkInsert(
                        connStr,
                        tableInfo.Schema,
                        tableInfo.FullTableName,
                        tableInfo.TableName, // use the extracted table name
                        aggregatedDataTable,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    insertSuccess = false;
                    insertError = ex.Message;
                }

                if (!insertSuccess)
                {
                    yield return $"Error during bulk insert: {insertError}";
                    yield break;
                }

                yield return $"Data successfully inserted into {tableInfo.FullTableName}.";
            }
            else
            {
                // Optionally, yield a final control message.
                // yield return "Data stream completed";
            }
        }

        // Updated asynchronous helper method to read from the channel
        private async Task<string> ReadFromChannel(ChannelReader<string> reader, CancellationToken token)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                if (!await reader.WaitToReadAsync(linkedCts.Token))
                {
                    Console.WriteLine("Channel completed");
                    return null;
                }

                if (reader.TryRead(out string result))
                {
                    Console.WriteLine($"Read chunk of size {result?.Length ?? 0}");
                    return result;
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
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading from channel: {ex.Message}");
                return null;
            }
        }

        private bool IsControlMessage(string message)
        {
            return message.StartsWith("Starting data stream from") ||
                   message.Equals("Starting query execution...") ||
                   message.Equals("Query execution completed") ||
                   message.Equals("Data stream completed") ||
                   message.StartsWith("Error:");
        }

        private void ProcessSchemaMessage(string message, bool isBulkInsert, ref string outputMessage)
        {
            // For schema messages you can choose what to yield.
            if (isBulkInsert)
            {
                outputMessage = "Received schema information";
            }
            else
            {
                // Optionally, for non-bulk mode you can ignore schema messages.
                outputMessage = "";
            }
        }

        // Modified to yield actual data for non-bulk mode
        private void ProcessBatchMessage(
            string message,
            bool isBulkInsert,
            ref DataTable aggregatedTable,
            ref int totalRows,
            ref int batchCount,
            ref string outputMessage,
            ref bool hasError,
            ref string errorMessage)
        {
            batchCount++;
            try
            {
                int firstColon = message.IndexOf(':', 6);
                if (firstColon > 0)
                {
                    // Extract the JSON that contains the batch data.
                    string batchJson = message.Substring(firstColon + 1);
                    DataTable batchData = JsonConvert.DeserializeObject<DataTable>(batchJson);
                    if (batchData != null)
                    {
                        if (isBulkInsert)
                        {
                            // Aggregate the data for bulk insert.
                            if (aggregatedTable == null)
                            {
                                aggregatedTable = batchData.Clone();
                            }
                            foreach (DataRow row in batchData.Rows)
                            {
                                aggregatedTable.ImportRow(row);
                            }
                            totalRows += batchData.Rows.Count;

                            // Optionally set a status update.
                            if (batchCount % 5 == 0)
                            {
                                outputMessage = $"Processed batch {batchCount} ({totalRows} rows so far)";
                            }
                        }
                        else
                        {
                            // For non-bulk mode return the actual data.
                            outputMessage = batchJson;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                hasError = true;
                errorMessage = ex.Message;
            }
        }

        private void ProcessSummaryMessage(string message, bool isBulkInsert, ref string outputMessage)
        {
            try
            {
                string summaryJson = message.Substring(8); // Remove "SUMMARY:" prefix
                var summary = JsonConvert.DeserializeObject<dynamic>(summaryJson);
                if (isBulkInsert)
                {
                    outputMessage = $"Received all data: {summary.batches} batches with {summary.totalRows} rows";
                }
                else
                {
                    // Optionally, in non-bulk mode you might not want to send a summary control message.
                    outputMessage = "";
                }
            }
            catch
            {
                // Ignore summary processing errors
            }
        }

        // This helper extracts schema and table name from your query.
        // It expects a query in the form: select * from database.schema.table
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

            // If there are three parts, assume it's database.schema.table
            return (parts[1], parts[2]);
        }

        // Returns the destination table info based on the query.
        // Your query is expected to contain all the necessary info, e.g.:
        // select * from database.schema.table
        private (bool Valid, string Schema, string TableName, string FullTableName, string ErrorMessage)
            GetDestinationTableInfo(AgentInfo agent, string queryIndex)
        {
            var result = (Valid: false, Schema: "", TableName: "", FullTableName: "", ErrorMessage: "");

            if (!int.TryParse(queryIndex, out int qIndex))
            {
                result.ErrorMessage = $"Error: queryIndex '{queryIndex}' is not a valid integer.";
                return result;
            }

            if (agent.Configuration == null ||
                agent.Configuration.Connections == null ||
                agent.Configuration.Connections.Count == 0 ||
                qIndex < 0 ||
                qIndex >= agent.Configuration.Connections[0].Queries.Count)
            {
                result.ErrorMessage = "Error: Invalid query configuration for bulk insert.";
                return result;
            }

            string query = agent.Configuration.Connections[0].Queries[qIndex];
            var schemaAndTable = ExtractSchemaAndTableName(query);
            if (schemaAndTable == null)
            {
                result.ErrorMessage = "Error: Could not extract schema and table name from the query.";
                return result;
            }

            result.Schema = schemaAndTable.Value.Item1;
            result.TableName = schemaAndTable.Value.Item2;
            result.FullTableName = $"[{result.Schema}].[{result.TableName}]";
            result.Valid = true;
            return result;
        }

        // Updated PerformBulkInsert now accepts the extracted tableName as a separate parameter.
        private async Task PerformBulkInsert(
            string connectionString,
            string schema,
            string fullTableName,
            string tableName, // new parameter representing the extracted table name
            DataTable dataTable,
            CancellationToken cancellationToken)
        {
            using (var sqlConn = new SqlConnection(connectionString))
            {
                await sqlConn.OpenAsync(cancellationToken);

                string createSchemaSql = $@"
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema}')
                BEGIN
                    EXEC('CREATE SCHEMA [{schema}]');
                END";
                using (var cmd = new SqlCommand(createSchemaSql, sqlConn))
                {
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                string dropTableSql = $@"
                IF OBJECT_ID('{fullTableName}', 'U') IS NOT NULL 
                DROP TABLE {fullTableName};";
                using (var cmd = new SqlCommand(dropTableSql, sqlConn))
                {
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Use the extracted tableName instead of dataTable.TableName
                string createTableSql = BuildCreateTableDDL(dataTable, schema, tableName);
                using (var cmd = new SqlCommand(createTableSql, sqlConn))
                {
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await BulkInsertWithPaging(sqlConn, dataTable, fullTableName, PageSize);
            }
        }

        // BuildCreateTableDDL builds the CREATE TABLE command using valid column names.
        private string BuildCreateTableDDL(DataTable dt, string schema, string tableName)
        {
            // Build each column definition and store in a list
            var columnDefinitions = new List<string>();
            int colIndex = 0;
            foreach (DataColumn col in dt.Columns)
            {
                // Use a default name if the column name is null, empty, or whitespace.
                string colName = string.IsNullOrWhiteSpace(col.ColumnName)
                    ? $"Column{colIndex}"
                    : col.ColumnName.Trim();

                string sqlType = MapType(col.DataType);
                // Format each column definition properly.
                columnDefinitions.Add($"    [{colName}] {sqlType} NULL");
                colIndex++;
            }

            // Join all column definitions with commas and proper line breaks.
            string columnsSql = string.Join(",\n", columnDefinitions);

            // Build the complete CREATE TABLE statement.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"CREATE TABLE [{schema}].[{tableName}] (");
            sb.AppendLine(columnsSql);
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
                    bulkCopy.BulkCopyTimeout = 300;
                    await bulkCopy.WriteToServerAsync(dtPage);
                }
            }
        }

        private string GetDestinationConnectionString(string destinationId)
        {
            var dest = DestinationStore.GetDestinationById(destinationId);
            return dest == null ? "" : dest.ConnectionString;
        }
    }
}
