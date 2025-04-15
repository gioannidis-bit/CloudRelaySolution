using CloudRelayService.Controllers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

public static class DestinationStore
{
    private static readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "destinations.json");

    public static List<DestinationConfig> Destinations { get; private set; } = LoadDestinations();

    public static List<DestinationConfig> LoadDestinations()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonConvert.DeserializeObject<List<DestinationConfig>>(json);
                return list ?? new List<DestinationConfig>();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error loading destinations: " + ex.Message);
            }
        }
        return new List<DestinationConfig>();
    }

    public static void SaveDestinations()
    {
        try
        {
            var json = JsonConvert.SerializeObject(Destinations, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error saving destinations: " + ex.Message);
        }
    }

    public static DestinationConfig GetDestinationById(string destinationId)
    {
        if (string.IsNullOrWhiteSpace(destinationId))
        {
            return null;
        }
        return Destinations?.Find(d => d.Id.Equals(destinationId, StringComparison.OrdinalIgnoreCase));
    }
}
