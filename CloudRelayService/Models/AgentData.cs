using System;
using System.ComponentModel.DataAnnotations;

namespace CloudRelayService.Models
{
    public class AgentData
    {
        [Key]
        public int Id { get; set; } // Auto-generated primary key

        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
