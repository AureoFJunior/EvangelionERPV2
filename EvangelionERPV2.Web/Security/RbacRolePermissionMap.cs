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

            RbacPermissions.SalesPerformance.ReadSelf,

            RbacPermissions.Pdv.Operate,
            RbacPermissions.Pdv.OpenSession,
            RbacPermissions.Pdv.CloseSession,
            RbacPermissions.Pdv.ApplyDiscount,

            RbacPermissions.Notifications.Read,

            RbacPermissions.Storefront.CategoriesRead,
        ];

        private static readonly string[] SupervisorPermissions =
        [
            ..EmployeePermissions,

            RbacPermissions.Metrics.Read,

            RbacPermissions.Ai.Chat,

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

            RbacPermissions.Pdv.CashMovement,
            RbacPermissions.Pdv.OverrideDiscount,
            RbacPermissions.Pdv.CancelSale,
            RbacPermissions.Pdv.VoidItem,
            RbacPermissions.Pdv.ReadReports,

            RbacPermissions.Quotes.Read,
            RbacPermissions.Quotes.Create,
            RbacPermissions.Quotes.Update,
            RbacPermissions.Quotes.Convert,
            RbacPermissions.Quotes.Delete,

            RbacPermissions.Reports.Read,
            RbacPermissions.Reports.Generate,

            RbacPermissions.Suppliers.Read,
            RbacPermissions.Suppliers.Create,
            RbacPermissions.Suppliers.Update
        ];

        private static readonly string[] ManagerPermissions =
        [
            ..SupervisorPermissions,

            // Only accounts below Manager: UserController.CanAssignLevel.
            RbacPermissions.Users.Read,
            RbacPermissions.Users.Create,
            RbacPermissions.Users.Update,

            RbacPermissions.Customers.Delete,

            RbacPermissions.Customers.CreditOverride,

            RbacPermissions.Accounting.Export,

            RbacPermissions.Suppliers.Delete,

            RbacPermissions.Products.ReadCost,

            RbacPermissions.SalesPerformance.Read,
            RbacPermissions.SalesPerformance.ManageGoals,

            RbacPermissions.BankReconciliation.Read,
            RbacPermissions.BankReconciliation.Import,
            RbacPermissions.BankReconciliation.Resolve,

            RbacPermissions.Dashboard.Read,

            RbacPermissions.Ai.Act,

            RbacPermissions.Bills.Settle,
            RbacPermissions.Bills.Reissue,

            RbacPermissions.Enterprise.ReadSelf,

            RbacPermissions.Employees.Read,
            RbacPermissions.Employees.Create,
            RbacPermissions.Employees.Update,
            RbacPermissions.Employees.Delete,
            RbacPermissions.Employees.UploadPicture,

            // RbacPermissions.EmployeeAbsences is deliberately NOT here. Absences sit a tier above
            // the employee record: an atestado carries a CID (LGPD art. 11 health data), and
            // IsJustified decides whether a day is paid — so they follow Payroll and stay
            // admin-only, reachable only through RbacPermissions.All.

            // RbacPermissions.EmployeeVacations is deliberately NOT here either, and for a
            // stronger reason: scheduling férias writes an absence row, and an accrual that
            // outruns its concessivo deadline is a debt the company owes twice over.

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

            RbacPermissions.Storefront.Manage,
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
