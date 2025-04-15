using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CloudRelayService.Hubs
{
    public static class AgentConfigurationStore
    {
        private static readonly string FilePath = "agentConfigurations.json";

        public static ConcurrentDictionary<string, AgentConfiguration> Configurations { get; private set; }
            = new ConcurrentDictionary<string, AgentConfiguration>();

        static AgentConfigurationStore()
        {
            LoadConfigurations();
        }

        public static void LoadConfigurations()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, AgentConfiguration>>(json);
                    if (dict != null)
                    {
                        Configurations = new ConcurrentDictionary<string, AgentConfiguration>(dict);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading configurations: " + ex.Message);
            }
        }

        public static void SaveConfigurations()
        {
            try
            {
                var dict = new Dictionary<string, AgentConfiguration>(Configurations);
                string json = JsonConvert.SerializeObject(dict, Formatting.Indented);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error saving configurations: " + ex.Message);
            }
        }

        public static void UpdateConfiguration(string agentId, AgentConfiguration config)
        {
            Configurations[agentId] = config;
            SaveConfigurations();
        }
    }
}
