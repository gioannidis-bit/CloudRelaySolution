using System.IO;
using System.Xml.Serialization;
using CloudRelayService.Models;

namespace CloudRelayService.Helpers
{
    public static class QueryFileHelper
    {
        private static string filePath = "App_Data/Queries.xml";

        public static Models.QueryCollection LoadQueries()
        {
            if (!File.Exists(filePath))
                return new Models.QueryCollection();

            var serializer = new XmlSerializer(typeof(Models.QueryCollection));
            using var stream = File.OpenRead(filePath);
            return (Models.QueryCollection)serializer.Deserialize(stream);
        }

        public static void SaveQueries(Models.QueryCollection queries)
        {
            var serializer = new XmlSerializer(typeof(Models.QueryCollection));
            using var stream = File.Create(filePath);
            serializer.Serialize(stream, queries);
        }
    }
}
