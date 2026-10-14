namespace EvangelionERPV2.Web.Security
{
    public static class RbacPermissions
    {
        public static IReadOnlyCollection<string> All { get; } =
        [
            Metrics.Read,
            Health.Read,
            Messaging.Redrive,

            Accounting.Export,

            Ai.Chat,
            Ai.Act,

            Notifications.Read,
            Notifications.Broadcast,

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

            SalesPerformance.Read,
            SalesPerformance.ReadSelf,
            SalesPerformance.ManageGoals,

            Products.Read,
            Products.Create,
            Products.Update,
            Products.Delete,
            Products.UploadPicture,
            Products.DeletePicture,
            Products.ReadCost,

            Pdv.Operate,
            Pdv.OpenSession,
            Pdv.CloseSession,
            Pdv.CashMovement,
            Pdv.ApplyDiscount,
            Pdv.OverrideDiscount,
            Pdv.CancelSale,
            Pdv.VoidItem,
            Pdv.ReadReports,

            Storefront.CatalogRead,
            Storefront.Checkout,
            Storefront.OrdersRead,
            Storefront.OrdersWrite,
            Storefront.Manage,
            Storefront.CategoriesRead,
            Storefront.CategoriesManage,

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

        /// <summary>
        /// Replaying dead-lettered messages. Admin only, and deliberately not granted to Manager:
        /// the work queues are not tenant-scoped, so one redrive replays messages belonging to every
        /// enterprise on the broker.
        /// </summary>
        public static class Messaging
        {
            public const string Redrive = "messaging.redrive";
        }

        public static class Health
        {
            public const string Read = "health.read";
        }

        public static class Accounting
        {
            public const string Export = "accounting.export";
        }

        public static class Ai
        {
            public const string Chat = "ai.chat";

            /// <summary>
            /// Lets the agent propose a write. The write itself still runs under the caller's own
            /// permission for it, and only after the caller confirms the card.
            /// </summary>
            public const string Act = "ai.act";
        }

        public static class Notifications
        {
            /// <summary>
            /// Employee tier. The feed is the bell every user carries; its *contents* are filtered
            /// per item against the caller's own permissions, so being allowed to open it grants
            /// nothing the caller could not already read elsewhere.
            /// </summary>
            public const string Read = "notifications.read";

            /// <summary>
            /// Admin only, alongside payroll and the fiscal certificate. A broadcast is an
            /// unsolicited message to everyone in the company, carrying the company's authority,
            /// with no document to check it against and no approval step.
            /// </summary>
            public const string Broadcast = "notifications.broadcast";
        }

        public static class Audit
        {
            public const string Read = "audit.read";
            public const string CleanupRetention = "audit.retention.cleanup";
        }

        public static class BankReconciliation
        {
            public const string Read = "bank-reconciliation.read";

            public const string Import = "bank-reconciliation.import";

            public const string Resolve = "bank-reconciliation.resolve";
        }

        public static class Dashboard
        {
            public const string Read = "dashboard.read";
        }

        public static class Bills
        {
            public const string Read = "bills.read";
            public const string Generate = "bills.generate";
            public const string DownloadPdf = "bills.pdf.download";

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
            public const string Read = "employee-absences.read";
            public const string Create = "employee-absences.create";
            public const string Update = "employee-absences.update";
            public const string Delete = "employee-absences.delete";
        }

        public static class EmployeeVacations
        {
            public const string Read = "employee-vacations.read";

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

            public const string SettingsRead = "nfe.settings.read";
            public const string SettingsUpdate = "nfe.settings.update";

            public const string CertificateManage = "nfe.certificate.manage";

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
            public const string Read = "payroll.read";
            public const string Generate = "payroll.generate";
            public const string UpdateSettings = "payroll.settings.update";
        }

        /// <summary>
        /// Metas e comissão. Manager tier: the screen shows every seller's numbers and what each is
        /// owed, which is company financial data, not an extension of being able to read orders.
        /// </summary>
        public static class SalesPerformance
        {
            public const string Read = "sales.performance.read";

            /// <summary>
            /// A seller's own row, and only their own. Deliberately not <see cref="Read"/> with a
            /// filter: that permission carries every other seller's numbers and the enterprise's
            /// total, and the point here is a seller seeing what they are owed.
            /// </summary>
            public const string ReadSelf = "sales.performance.read.self";

            public const string ManageGoals = "sales.goals.manage";
        }

        public static class Products
        {
            public const string Read = "products.read";
            public const string Create = "products.create";
            public const string Update = "products.update";
            public const string Delete = "products.delete";
            public const string UploadPicture = "products.picture.upload";
            public const string DeletePicture = "products.picture.delete";

            /// <summary>
            /// What the company pays for its goods, and therefore what it earns on every sale. Not
            /// an [Authorize] on any action — it decides whether the cost and margin fields survive
            /// into ProductDTO and OrderDTO, which is why the coverage test does not list it.
            /// </summary>
            public const string ReadCost = "products.cost.read";
        }

        /// <summary>
        /// The till. Split finely on purpose: the operator who rings sales all day is not the
        /// person who should be able to open the drawer for a sangria or void a completed sale.
        /// </summary>
        public static class Pdv
        {
            /// <summary>Ring up and sync sales. The floor operator's permission.</summary>
            public const string Operate = "pdv.operate";

            public const string OpenSession = "pdv.session.open";
            public const string CloseSession = "pdv.session.close";

            /// <summary>Sangria and suprimento — taking cash out of the drawer, or adding it.</summary>
            public const string CashMovement = "pdv.cash.movement";

            /// <summary>A discount within whatever the shop lets an operator give.</summary>
            public const string ApplyDiscount = "pdv.discount.apply";

            /// <summary>A discount past that, which is why it is a separate, supervisor-tier grant.</summary>
            public const string OverrideDiscount = "pdv.discount.override";

            public const string CancelSale = "pdv.sale.cancel";

            /// <summary>
            /// Void a line from the sale being rung. Supervisor tier, and separate from
            /// <see cref="CancelSale"/>: cancelling a recorded sale leaves a reversal anyone can
            /// audit, whereas removing a line before payment leaves nothing behind at all — which
            /// is exactly why it is the commonest shrinkage route at a till.
            /// </summary>
            public const string VoidItem = "pdv.item.void";

            /// <summary>The X and Z readings, which show the day's takings.</summary>
            public const string ReadReports = "pdv.reports.read";
        }

        /// <summary>
        /// Limen, the public marketplace. The first three are held only by the storefront's service
        /// token — see <see cref="StorefrontTokenScope"/>; no role grants them, Admin included.
        /// </summary>
        public static class Storefront
        {
            public const string CatalogRead = "storefront.catalog.read";
            public const string Checkout = "storefront.checkout";
            public const string OrdersRead = "storefront.orders.read";

            /// <summary>Links, unlinks and withdraws a shopper's purchases.</summary>
            public const string OrdersWrite = "storefront.orders.write";

            /// <summary>Staff: list the own enterprise on the storefront and publish its products.</summary>
            public const string Manage = "storefront.manage";

            /// <summary>Staff: read Limen's category tree to file products under it.</summary>
            public const string CategoriesRead = "storefront.categories.read";

            /// <summary>Edit Limen's category tree. Held by Admin, but the endpoints also require the platform enterprise.</summary>
            public const string CategoriesManage = "storefront.categories.manage";
        }

        public static class Quotes
        {
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
