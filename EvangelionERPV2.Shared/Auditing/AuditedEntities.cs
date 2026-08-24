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
            typeof(EnterpriseFiscalSettings),

            // The period lock lives here: the one setting that freezes the books cannot be the one
            // with no trail, and reopening a closed month matters more than closing it.
            typeof(Enterprise),

            // Absences are the evidence behind a payroll amount and an advertência is a document
            // with legal weight in a labour dispute, so who flipped IsJustified — and when — has
            // to be recoverable. AuditTrailEntryFactory redacts the CID and the reason text out of
            // the trail: the trail records that a health record changed, never its contents.
            typeof(EmployeeAbsence)
        };

        private static readonly HashSet<string> _entityNames = new(
            _types.Select(type => type.Name),
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The audited entity names as a collection, for use inside an EF query.
        /// </summary>
        /// <remarks>
        /// <see cref="Contains(string)"/> is a custom static method, and EF cannot translate one:
        /// a query filtering on it throws <c>InvalidOperationException</c> at execution time rather
        /// than failing to compile. Filtering on this collection translates to an <c>IN (...)</c>.
        /// Matching is case-insensitive here and follows the database collation in SQL, which for
        /// this schema is also case-insensitive.
        /// </remarks>
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
