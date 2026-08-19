using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.Auditing
{
    public static class AuditedEntities
    {
        private static readonly HashSet<Type> _types = new()
        {
            typeof(Customer),
            typeof(Product),
            typeof(Order),
            typeof(Bill),
            typeof(PayableBill),
            typeof(PayableBillProduct),

            // Fiscal documents have legal weight and their issuance and cancellation are
            // irreversible, so every one needs a who-did-what trail. Property-level redaction in
            // AuditTrailEntryFactory keeps the access key, XML and QR data out of the trail.
            typeof(NFeDocument),

            // Admin-only settings whose edits change the tax treatment of every future document.
            typeof(EnterpriseFiscalSettings)
        };

        private static readonly HashSet<string> _entityNames = new(
            _types.Select(type => type.Name),
            StringComparer.OrdinalIgnoreCase);

        public static bool Contains(Type entityType)
        {
            return _types.Contains(entityType);
        }

        public static bool Contains(string entityName)
        {
            return _entityNames.Contains(entityName);
        }
    }
}
