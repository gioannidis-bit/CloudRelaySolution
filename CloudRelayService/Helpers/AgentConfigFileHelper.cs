using System.IO;
using System.Xml.Serialization;
using CloudRelayService.Models;

namespace CloudRelayService.Helpers
{
    public static class AgentConfigFileHelper
    {
        private static string filePath = "App_Data/AgentConfigs.xml";

        public static AgentConfigCollection LoadAgentConfigs()
        {
            if (!File.Exists(filePath))
                return new AgentConfigCollection();

            var serializer = new XmlSerializer(typeof(AgentConfigCollection));
            using var stream = File.OpenRead(filePath);
            return (AgentConfigCollection)serializer.Deserialize(stream);
        }

        public static void SaveAgentConfigs(AgentConfigCollection configs)
        {
            var serializer = new XmlSerializer(typeof(AgentConfigCollection));
            using var stream = File.Create(filePath);
            serializer.Serialize(stream, configs);
        }
    }
}
