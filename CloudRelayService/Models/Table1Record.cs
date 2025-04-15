using System;
using System.ComponentModel.DataAnnotations;

namespace CloudRelayService.Models
{
    public class Table1Record
    {
        // Use the proper key based on your existing table.
       
        public int Id { get; set; }

        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
