using System.Collections.Generic;
using System.Xml.Serialization;

namespace CloudRelayService.Models
{
    [XmlRoot("StoredQueries")]
    public class Models.QueryCollection
    {
        [XmlElement("StoredQuery")]
        public List<StoredQuery> Queries { get; set; } = new List<StoredQuery>();
    }
}
