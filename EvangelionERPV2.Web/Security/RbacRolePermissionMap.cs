using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Web.Security
{
    public static class RbacRolePermissionMap
    {
        private static readonly string[] EmployeePermissions =
        [
            RbacPermissions.Health.Read,

            RbacPermissions.Users.ReadSelf,
            RbacPermissions.Users.UpdateSelf,

            RbacPermissions.Orders.Read,
            RbacPermissions.Orders.Create,
        ];

        private static readonly string[] SupervisorPermissions =
        [
            ..EmployeePermissions,

            RbacPermissions.Metrics.Read,

            RbacPermissions.Audit.Read,

            RbacPermissions.Bills.Read,
            RbacPermissions.Bills.Generate,
            RbacPermissions.Bills.DownloadPdf,

            RbacPermissions.CashFlowForecast.Read,

            RbacPermissions.CashFlowForecast.Simulate,

            RbacPermissions.Customers.Read,
            RbacPermissions.Customers.Create,
            RbacPermissions.Customers.Update,

            RbacPermissions.NFe.Read,
            RbacPermissions.NFe.Issue,
            RbacPermissions.NFe.Cancel,

            RbacPermissions.Orders.Update,
            RbacPermissions.Orders.Refund,

            RbacPermissions.Products.Read,
            RbacPermissions.Products.Create,
            RbacPermissions.Products.Update,
            RbacPermissions.Products.Delete,
            RbacPermissions.Products.UploadPicture,
            RbacPermissions.Products.DeletePicture,

            RbacPermissions.Reports.Read,
            RbacPermissions.Reports.Generate
        ];

        private static readonly string[] ManagerPermissions =
        [
            ..SupervisorPermissions,

            RbacPermissions.Customers.Delete,

            // Enterprise self-read exposes company-level financials (CurrentBalance), so it
            // starts at Manager instead of the base role set.
            RbacPermissions.Enterprise.ReadSelf,

            // Employee records carry LGPD-sensitive data (document, salary), so the whole
            // permission group starts at Manager instead of Supervisor.
            RbacPermissions.Employees.Read,
            RbacPermissions.Employees.Create,
            RbacPermissions.Employees.Update,
            RbacPermissions.Employees.Delete,
            RbacPermissions.Employees.UploadPicture,

            // Certificate metadata only — subject, expiry, days remaining. Whoever operates
            // issuance needs the expiry warning without waiting on an admin; uploading and
            // replacing the certificate stays admin-only.
            RbacPermissions.NFe.CertificateRead,

            RbacPermissions.PayableBills.Read,
            RbacPermissions.PayableBills.Create,
            RbacPermissions.PayableBills.Update,
            RbacPermissions.PayableBills.MarkProductsReceived,
            RbacPermissions.PayableBills.Refund,

            RbacPermissions.Opportunities.Read,
            RbacPermissions.Opportunities.Feedback,
            RbacPermissions.Opportunities.Recompute,

            RbacPermissions.Orders.Delete,
        ];

        private static readonly IReadOnlyDictionary<EnumAccessLevel, IReadOnlySet<string>> PermissionsByRole =
            new Dictionary<EnumAccessLevel, IReadOnlySet<string>>
            {
                [EnumAccessLevel.Admin] = BuildSet(RbacPermissions.All),
                [EnumAccessLevel.Manager] = BuildSet(ManagerPermissions),
                [EnumAccessLevel.Supervisor] = BuildSet(SupervisorPermissions),
                [EnumAccessLevel.Employee] = BuildSet(EmployeePermissions)
            };

        public static bool HasPermission(short accessLevel, string permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
                return false;

            if (!Enum.IsDefined(typeof(EnumAccessLevel), (int)accessLevel))
                return false;

            var role = (EnumAccessLevel)accessLevel;
            return PermissionsByRole.TryGetValue(role, out var permissions) && permissions.Contains(permission);
        }

        public static IReadOnlyCollection<string> GetPermissions(short accessLevel)
        {
            if (!Enum.IsDefined(typeof(EnumAccessLevel), (int)accessLevel))
                return [];

            var role = (EnumAccessLevel)accessLevel;
            return PermissionsByRole.TryGetValue(role, out var permissions)
                ? permissions.ToArray()
                : [];
        }

        private static HashSet<string> BuildSet(IEnumerable<string> permissions)
        {
            return new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
        }
    }
}
