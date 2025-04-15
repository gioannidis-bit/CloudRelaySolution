namespace OutboundAgent.Models
{
    public class QueryRequest
    {
        public string Id { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string QueryIndex { get; set; } = string.Empty;
        public string? DestinationId { get; set; }
    }
}