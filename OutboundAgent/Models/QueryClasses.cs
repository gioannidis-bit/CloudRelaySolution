namespace OutboundAgent.Models
{
    public class QueryRequest
    {
        public string Id { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string QueryIndex { get; set; } = string.Empty;
        public string? DestinationId { get; set; }
    }

    public class QueryDataChunk
    {
        public string QueryId { get; set; } = string.Empty;
        public string ChunkId { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public bool IsLastChunk { get; set; }
        public bool IsSchema { get; set; } = false;
    }
}