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

            typeof(NFeDocument),

            typeof(EnterpriseFiscalSettings),

            typeof(Enterprise),

            typeof(EmployeeAbsence)
        };

        private static readonly HashSet<string> _entityNames = new(
            _types.Select(type => type.Name),
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The audited entity names as a collection, for use inside an EF query.
        /// </summary>
        public static IReadOnlyCollection<string> Names => _entityNames;

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
