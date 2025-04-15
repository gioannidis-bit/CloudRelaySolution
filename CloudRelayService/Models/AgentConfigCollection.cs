using System.Collections.Generic;
using System.Xml.Serialization;

namespace CloudRelayService.Models
{
    [XmlRoot("AgentConfigs")]
    public class AgentConfigCollection
    {
        [XmlElement("AgentConfig")]
        public List<AgentConfig> Configs { get; set; } = new List<AgentConfig>();
    }
}
