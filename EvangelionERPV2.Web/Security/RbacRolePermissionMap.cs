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

            // The floor operator. Opens and closes their own till and rings sales, which is the
            // whole job — but cannot open the drawer for a sangria or void a completed sale.
            RbacPermissions.Pdv.Operate,
            RbacPermissions.Pdv.OpenSession,
            RbacPermissions.Pdv.CloseSession,
            RbacPermissions.Pdv.ApplyDiscount,
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

            // Quoting is selling: same tier as creating the order it becomes.
            // Supervisor tier: cash leaving the drawer, a discount past the operator's ceiling,
            // voiding a sale that already happened, and the readings that show the day's takings.
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

            // A supplier is the mirror image of a customer — a counterparty record with a
            // document that never leaves the API — so it sits at the customer tier, not the
            // employee one.
            RbacPermissions.Suppliers.Read,
            RbacPermissions.Suppliers.Create,
            RbacPermissions.Suppliers.Update
        ];

        private static readonly string[] ManagerPermissions =
        [
            ..SupervisorPermissions,

            RbacPermissions.Customers.Delete,

            // Overriding a credit limit is a decision about the company's exposure, so it sits
            // with the tier that already sees the carteira and the payables.
            RbacPermissions.Customers.CreditOverride,

            // The accountant's monthly package is Manager data by construction: it is the payables
            // and the bank-facing side of the month, and the payroll inside it is aggregated so it
            // never carries an individual's salary the way admin-only payroll.read does.
            RbacPermissions.Accounting.Export,

            RbacPermissions.Suppliers.Delete,

            // What a unit costs is what the company earns on it. A Supervisor sells and quotes
            // against the price list; the margin behind that price is the same class of company
            // financial as CurrentBalance and the payables, so it starts here.
            RbacPermissions.Products.ReadCost,

            // Same tier and the same reason: one screen carrying every seller's numbers and what
            // each of them is owed.
            RbacPermissions.SalesPerformance.Read,
            RbacPermissions.SalesPerformance.ManageGoals,

            // A bank statement exposes the company's whole cash position and every counterparty it
            // moved money with, so it sits at the tier that already sees CurrentBalance and the
            // payables — not with customers and products at Supervisor.
            RbacPermissions.BankReconciliation.Read,
            RbacPermissions.BankReconciliation.Import,
            RbacPermissions.BankReconciliation.Resolve,

            // The daily panel puts payables and uncleared bank lines on the same screen as
            // receivables, so it sits at the tier that already sees both.
            RbacPermissions.Dashboard.Read,

            // Declaring a boleto paid and superseding one are financial assertions nothing else
            // verifies until CNAB retorno exists, so they start at Manager while issuing a boleto
            // stays at Supervisor.
            RbacPermissions.Bills.Settle,
            RbacPermissions.Bills.Reissue,

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

            // RbacPermissions.EmployeeAbsences is deliberately NOT here. Absences sit a tier above
            // the employee record: an atestado carries a CID (LGPD art. 11 health data), and
            // IsJustified decides whether a day is paid — so they follow Payroll and stay
            // admin-only, reachable only through RbacPermissions.All.

            // RbacPermissions.EmployeeVacations is deliberately NOT here either, and for a
            // stronger reason: scheduling férias writes an absence row, and an accrual that
            // outruns its concessivo deadline is a debt the company owes twice over.

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
