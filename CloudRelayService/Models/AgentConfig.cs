namespace CloudRelayService.Models
{
    public class AgentConfig
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ConnectionString { get; set; }
        public string TablesOrViews { get; set; }
    }
}
