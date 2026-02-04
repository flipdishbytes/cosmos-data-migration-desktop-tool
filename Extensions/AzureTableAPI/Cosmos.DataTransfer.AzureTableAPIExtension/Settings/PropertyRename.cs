namespace Cosmos.DataTransfer.AzureTableAPIExtension.Settings;

/// <summary>
/// Defines a single property rename when writing to the sink (e.g. "id" → "entityId" for Cosmos DB Table API reserved names).
/// </summary>
public class PropertyRename
{
    /// <summary>Source property name (case-insensitive).</summary>
    public string? From { get; set; }

    /// <summary>Target property name when writing to the sink.</summary>
    public string? To { get; set; }
}
