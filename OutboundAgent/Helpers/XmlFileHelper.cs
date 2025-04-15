using System.Xml.Serialization;

public static class XmlFileHelper
{
    public static T Load<T>(string filePath)
    {
        if (!File.Exists(filePath))
            return Activator.CreateInstance<T>();

        var serializer = new XmlSerializer(typeof(T));
        using var stream = new FileStream(filePath, FileMode.Open);
        return (T)serializer.Deserialize(stream);
    }

    public static void Save<T>(string filePath, T data)
    {
        var serializer = new XmlSerializer(typeof(T));
        using var writer = new StreamWriter(filePath);
        serializer.Serialize(writer, data);
    }
}
