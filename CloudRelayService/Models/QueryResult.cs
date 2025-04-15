namespace CloudRelayService.Models
{
    public class QueryResult
    {
        public string Id { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }
}