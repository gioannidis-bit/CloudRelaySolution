using System.Xml.Serialization;

public class AgentConfig
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string ConnectionString { get; set; }
    public List<StoredQuery> Queries { get; set; } = new();
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
}

public class StoredQuery
{
    public int Id { get; set; }
    public string QueryText { get; set; }
    public string Description { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
}

[XmlRoot("AgentConfigs")]
public class AgentConfigCollection
{
    [XmlElement("AgentConfig")]
    public List<AgentConfig> AgentConfigs { get; set; } = new();
}
