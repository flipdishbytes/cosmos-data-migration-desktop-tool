namespace Cosmos.DataTransfer.AzureTableAPIExtension.Settings
{
    public class AzureTableAPIDataSinkSettings : AzureTableAPISettingsBase
    {
        /// <summary>
        /// The Maximum number of concurrent entity writes to the Azure Table API.
        /// This setting is used to control the number of concurrent writes to the Azure Table API.
        /// </summary>
        public int? MaxConcurrentEntityWrites { get; set; }
        
        /// <summary>
        /// Specifies the behavior when writing entities to the table.
        /// Create: Adds new entities only, fails if entity already exists (default).
        /// Replace: Upserts entities, completely replacing existing ones.
        /// Merge: Upserts entities, merging properties with existing ones.
        /// </summary>
        public EntityWriteMode? WriteMode { get; set; } = EntityWriteMode.Create;

        /// <summary>
        /// When writing to Cosmos DB Table API, the property name "id" (and "ID", "Id") is reserved and not supported.
        /// Set this to a different property name (e.g. "entityId" or "idValue") to store the source "id" value under that name instead.
        /// If not set and the source has an "id" property, writes to Cosmos DB Table API will fail with PropertyNameInvalid.
        /// Deprecated in favor of <see cref="PropertyRenames"/>; if both are set, PropertyRenames takes precedence for "id".
        /// </summary>
        public string? IdPropertyRename { get; set; }

        /// <summary>
        /// List of property renames when writing to the sink. Use for Cosmos DB Table API reserved names (id, etag, rid, ResourceId)
        /// or any source property you want to write under a different name.
        /// Example: [ { "From": "id", "To": "entityId" }, { "From": "etag", "To": "entityEtag" } ]
        /// </summary>
        public List<PropertyRename>? PropertyRenames { get; set; }
    }
}