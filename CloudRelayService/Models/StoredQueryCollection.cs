using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace CloudRelayService.Models
{

    [Serializable]
    [XmlRoot("StoredQueries")]
    public class StoredQueryCollection
    {
        [XmlElement("StoredQuery")]
        public List<StoredQuery> Queries { get; set; } = new List<StoredQuery>();
    }
}