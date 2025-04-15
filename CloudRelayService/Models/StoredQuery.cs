namespace CloudRelayService.Models
{
    public class StoredQuery
    {
        public int Id { get; set; }
        public string QueryText { get; set; }
        public string Description { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}
