namespace OutboundAgent.Models
{
    public class QueryDataChunk
    {
        public string QueryId { get; set; } = string.Empty;
        public string ChunkId { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public bool IsLastChunk { get; set; }
    }
}