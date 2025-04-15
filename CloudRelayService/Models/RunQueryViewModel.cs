using System;
using System.Collections.Generic;

namespace CloudRelayService.Models
{
    public class RunQueryViewModel
    {
        public StoredQuery Query { get; set; }
        public List<Dictionary<string, object>> Results { get; set; }
        public string ErrorMessage { get; set; }
    }
}
