namespace EvangelionERPV2.Web.Security
{
    public static class RbacPermissions
    {
        public static IReadOnlyCollection<string> All { get; } =
        [
            Metrics.Read,
            Health.Read,

            Accounting.Export,

            Audit.Read,
            Audit.CleanupRetention,

            BankReconciliation.Read,
            BankReconciliation.Import,
            BankReconciliation.Resolve,

            Dashboard.Read,

            Bills.Read,
            Bills.Generate,
            Bills.DownloadPdf,
            Bills.Settle,
            Bills.Reissue,

            CashFlowForecast.Read,
            CashFlowForecast.Simulate,

            Customers.Read,
            Customers.Create,
            Customers.Update,
            Customers.Delete,
            Customers.CreditOverride,

            Employees.Read,
            Employees.Create,
            Employees.Update,
            Employees.Delete,
            Employees.UploadPicture,

            EmployeeAbsences.Read,
            EmployeeAbsences.Create,
            EmployeeAbsences.Update,
            EmployeeAbsences.Delete,

            EmployeeVacations.Read,
            EmployeeVacations.Schedule,

            Email.Manage,
            Email.Send,

            Enterprise.Read,
            Enterprise.ReadSelf,
            Enterprise.Create,
            Enterprise.Update,
            Enterprise.Delete,
            Enterprise.Manage,

            NFe.Read,
            NFe.Issue,
            NFe.Cancel,
            NFe.SettingsRead,
            NFe.SettingsUpdate,
            NFe.CertificateManage,
            NFe.CertificateRead,

            Opportunities.Read,
            Opportunities.Feedback,
            Opportunities.Recompute,

            Orders.Read,
            Orders.Create,
            Orders.Update,
            Orders.Refund,
            Orders.Delete,

            PayableBills.Read,
            PayableBills.Create,
            PayableBills.Update,
            PayableBills.MarkProductsReceived,
            PayableBills.Refund,
            PayableBills.Delete,

            Payroll.Read,
            Payroll.Generate,
            Payroll.UpdateSettings,

            Quotes.Read,
            Quotes.Create,
            Quotes.Update,
            Quotes.Convert,
            Quotes.Delete,

            Products.Read,
            Products.Create,
            Products.Update,
            Products.Delete,
            Products.UploadPicture,
            Products.DeletePicture,

            Reports.Read,
            Reports.Generate,

            Suppliers.Read,
            Suppliers.Create,
            Suppliers.Update,
            Suppliers.Delete,

            Users.Read,
            Users.ReadSelf,
            Users.Create,
            Users.Update,
            Users.UpdateSelf,
            Users.Delete,
            Users.Manage
        ];

        public static class Metrics
        {
            public const string Read = "metrics.read";
        }

        public static class Health
        {
            public const string Read = "health.read";
        }

        public static class Accounting
        {
            // Manager tier, the same floor as PayableBills.Read and BankReconciliation.Read: the
            // monthly package is every receivable, payable, nota and stock movement of a month in
            // one file. It is a read — the export writes nothing — but it leaves the building as an
            // attachment, which is why payroll appears in it aggregated and never per employee.
            public const string Export = "accounting.export";
        }

        public static class Audit
        {
            public const string Read = "audit.read";
            public const string CleanupRetention = "audit.retention.cleanup";
        }

        public static class BankReconciliation
        {
            // Manager tier, the same floor as Enterprise.ReadSelf and PayableBills.Read. A bank
            // statement is the full cash position of the company plus every counterparty it paid
            // or was paid by, so it does not belong at the Supervisor tier that sees customers and
            // products. It is not admin-only either: unlike payroll.* it carries no individual's
            // personal data, and unlike nfe.settings.* it changes nothing — the import cannot move
            // a balance, only report a disagreement with one.
            public const string Read = "bank-reconciliation.read";

            // Import is a write, but a contained one: it appends statement lines and never touches
            // Enterprise.CurrentBalance. Kept at the same tier as Read because whoever reconciles
            // is whoever uploads, and splitting them would only mean nobody can finish the job.
            public const string Import = "bank-reconciliation.import";

            // Resolving is the human half of the same job as Import — the import leaves the lines
            // it could not judge, and this clears them. Same tier for the same reason: whoever
            // uploads the statement is whoever works the leftovers, and a split would leave the
            // list permanently full. Still a contained write: it labels evidence, it does not move
            // money or touch Enterprise.CurrentBalance.
            public const string Resolve = "bank-reconciliation.resolve";
        }

        public static class Dashboard
        {
            // Manager, the same floor as PayableBills.Read and BankReconciliation.Read. The panel
            // aggregates payables and the bank's uncleared lines alongside receivables, so a
            // Supervisor-tier panel would show a Supervisor two things that tier deliberately does
            // not see. Suppressing those two sections per caller was the alternative and it costs
            // more branching than an endpoint that either answers or does not.
            public const string Read = "dashboard.read";
        }

        public static class Bills
        {
            public const string Read = "bills.read";
            public const string Generate = "bills.generate";
            public const string DownloadPdf = "bills.pdf.download";

            // Money-state writes, split from Generate: issuing a document and declaring it paid
            // are different levels of trust, and Settle is the one that moves the cash-flow
            // forecast. Granted to Manager and above, never to Supervisor.
            public const string Settle = "bills.settle";
            public const string Reissue = "bills.reissue";
        }

        public static class CashFlowForecast
        {
            public const string Read = "cashflow.forecast.read";
            public const string Simulate = "cashflow.forecast.simulate";
        }

        public static class Customers
        {
            public const string Read = "customers.read";
            public const string Create = "customers.create";
            public const string Update = "customers.update";
            public const string Delete = "customers.delete";

            // Manager tier. Taking an order that puts a customer past the limit somebody set is a
            // decision about the company's money, not a sales action — the same reason settling a
            // boleto starts at Manager while issuing one stays at Supervisor.
            public const string CreditOverride = "customers.credit.override";
        }

        public static class Employees
        {
            public const string Read = "employees.read";
            public const string Create = "employees.create";
            public const string Update = "employees.update";
            public const string Delete = "employees.delete";
            public const string UploadPicture = "employees.picture.upload";
        }

        public static class EmployeeAbsences
        {
            // Admin-only group (granted via RbacPermissions.All, never mapped to another role),
            // one tier above Employees rather than beside it. Two reasons: an atestado row carries
            // a CID, which is LGPD art. 11 special-category health data, and IsJustified is what
            // decides whether a day is paid — so write access here is write access to payroll,
            // which is already admin-only.
            public const string Read = "employee-absences.read";
            public const string Create = "employee-absences.create";
            public const string Update = "employee-absences.update";
            public const string Delete = "employee-absences.delete";
        }

        public static class EmployeeVacations
        {
            // Admin-only for the same reason as EmployeeAbsences, granted through
            // RbacPermissions.All and never mapped to another role. Scheduling férias writes an
            // absence row and moves a statutory entitlement, so it is write access to both payroll
            // and a legal obligation; and a lapsed accrual is a debt owed in dobro, which makes the
            // read side a liability report rather than a calendar.
            public const string Read = "employee-vacations.read";

            // One write permission, not four. Schedule, sell and refresh are the same authority
            // over the same ledger — splitting them would grant nothing anyone would grant apart.
            public const string Schedule = "employee-vacations.schedule";
        }

        public static class Email
        {
            public const string Manage = "email.manage";
            public const string Send = "email.send";
        }

        public static class Enterprise
        {
            public const string Read = "enterprise.read";
            // Read the user's own enterprise (tenant from token claim) — all roles.
            public const string ReadSelf = "enterprise.self.read";
            public const string Create = "enterprise.create";
            public const string Update = "enterprise.update";
            public const string Delete = "enterprise.delete";
            public const string Manage = "enterprise.manage";
        }

        public static class NFe
        {
            public const string Read = "nfe.read";
            public const string Issue = "nfe.issue";
            public const string Cancel = "nfe.cancel";

            // Admin-only pair (present in RbacPermissions.All, deliberately absent from every
            // role array): the fiscal profile exposes the emitente CNPJ and IE, and a wrong
            // CFOP, CST or CRT silently mis-taxes every future document — a mistake only
            // fixable by cancelling inside the legal window. Same risk class as payroll.*.
            public const string SettingsRead = "nfe.settings.read";
            public const string SettingsUpdate = "nfe.settings.update";

            // Also admin-only by omission: the holder can replace the company's digital signing
            // identity, and a wrong certificate stops issuance for the whole tenant. Rotated
            // roughly once a year, so there is no operational reason to grant it wider.
            public const string CertificateManage = "nfe.certificate.manage";

            // Metadata only (subject, expiry, days remaining) — no key material, no password.
            // Granted down to Manager so whoever operates can see "12 days left" without
            // waiting on an admin.
            public const string CertificateRead = "nfe.certificate.read";
        }

        public static class Opportunities
        {
            public const string Read = "opportunities.read";
            public const string Feedback = "opportunities.feedback";
            public const string Recompute = "opportunities.recompute";
        }

        public static class Orders
        {
            public const string Read = "orders.read";
            public const string Create = "orders.create";
            public const string Update = "orders.update";
            public const string Refund = "orders.refund";
            public const string Delete = "orders.delete";
        }

        public static class PayableBills
        {
            public const string Read = "payable-bills.read";
            public const string Create = "payable-bills.create";
            public const string Update = "payable-bills.update";
            public const string MarkProductsReceived = "payable-bills.products.receive";
            public const string Refund = "payable-bills.refund";
            public const string Delete = "payable-bills.delete";
        }

        public static class Payroll
        {
            // Admin-only group (granted via RbacPermissions.All, never mapped to other
            // roles): payroll data exposes individual salaries (LGPD) and settings
            // changes alter every future payroll payable amount.
            public const string Read = "payroll.read";
            public const string Generate = "payroll.generate";
            public const string UpdateSettings = "payroll.settings.update";
        }

        public static class Products
        {
            public const string Read = "products.read";
            public const string Create = "products.create";
            public const string Update = "products.update";
            public const string Delete = "products.delete";
            public const string UploadPicture = "products.picture.upload";
            public const string DeletePicture = "products.picture.delete";
        }

        public static class Quotes
        {
            // Supervisor tier, with the rest of selling. A quote reserves nothing and moves no
            // money; converting one creates an ordinary order, which a Supervisor may already do.
            public const string Read = "quotes.read";
            public const string Create = "quotes.create";
            public const string Update = "quotes.update";
            public const string Convert = "quotes.convert";
            public const string Delete = "quotes.delete";
        }

        public static class Reports
        {
            public const string Read = "reports.read";
            public const string Generate = "reports.generate";
        }

        public static class Suppliers
        {
            public const string Read = "suppliers.read";
            public const string Create = "suppliers.create";
            public const string Update = "suppliers.update";
            public const string Delete = "suppliers.delete";
        }

        public static class Users
        {
            public const string Read = "users.read";
            public const string ReadSelf = "users.self.read";
            public const string Create = "users.create";
            public const string Update = "users.update";
            public const string UpdateSelf = "users.self.update";
            public const string Delete = "users.delete";
            public const string Manage = "users.manage";
        }
    }
}
