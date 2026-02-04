using System.Collections.Generic;
using Azure.Data.Tables;
using Cosmos.DataTransfer.Interfaces;

namespace Cosmos.DataTransfer.AzureTableAPIExtension.Data
{
    public static class AzureTableAPIDataItemExtensions
    {
        private static readonly StringComparison PropertyNameComparison = StringComparison.InvariantCultureIgnoreCase;

        /// <summary>
        /// Builds a TableEntity from an IDataItem. PartitionKey and RowKey are mapped by name; any property in propertyRenames (key = source name, case-insensitive) is written under the value name.
        /// Reserved Cosmos DB Table API properties (id, etag, rid, ResourceId) not in propertyRenames are skipped so the sink does not fail.
        /// </summary>
        public static TableEntity ToTableEntity(
            this IDataItem item,
            string? partitionKeyFieldName,
            string? rowKeyFieldName,
            IReadOnlyDictionary<string, string>? propertyRenames = null)
        {
            var entity = new TableEntity();

            var partitionKeyFieldNameToUse = "PartitionKey";
            if (!string.IsNullOrWhiteSpace(partitionKeyFieldName))
                partitionKeyFieldNameToUse = partitionKeyFieldName;

            var rowKeyFieldNameToUse = "RowKey";
            if (!string.IsNullOrWhiteSpace(rowKeyFieldName))
                rowKeyFieldNameToUse = rowKeyFieldName;

            foreach (var key in item.GetFieldNames())
            {
                if (key.Equals(partitionKeyFieldNameToUse, PropertyNameComparison))
                {
                    entity.PartitionKey = item.GetValue(key)?.ToString();
                }
                else if (key.Equals(rowKeyFieldNameToUse, PropertyNameComparison))
                {
                    entity.RowKey = item.GetValue(key)?.ToString();
                }
                else
                {
                    var newName = GetRenamedPropertyName(key, propertyRenames);
                    if (newName != null)
                        entity.Add(newName, item.GetValue(key));
                    // If null, property is reserved (e.g. id) and not renamed – skip so Cosmos DB Table API does not fail
                }
            }

            return entity;
        }

        /// <returns>New name if the property should be written (possibly renamed); null if it should be skipped (reserved, no rename).</returns>
        private static string? GetRenamedPropertyName(string sourceName, IReadOnlyDictionary<string, string>? propertyRenames)
        {
            if (propertyRenames != null)
            {
                foreach (var kv in propertyRenames)
                {
                    if (string.Equals(kv.Key, sourceName, PropertyNameComparison) && !string.IsNullOrWhiteSpace(kv.Value))
                        return kv.Value;
                }
            }

            // Cosmos DB Table API reserved names – skip if no rename configured
            if (IsReservedPropertyName(sourceName))
                return null;

            return sourceName;
        }

        private static bool IsReservedPropertyName(string name)
        {
            return name.Equals("id", PropertyNameComparison)
                || name.Equals("etag", PropertyNameComparison)
                || name.Equals("rid", PropertyNameComparison)
                || name.Equals("ResourceId", PropertyNameComparison);
        }
    }
}
