using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Security.Claims;

namespace EvangelionERPV2.Shared.Context
{
    public class AppDbContext : DbContext
    {
        private readonly IHttpContextAccessor? _httpContextAccessor;
        private readonly IAuditTrailEntryFactory? _auditTrailEntryFactory;

        public AppDbContext(
            DbContextOptions<AppDbContext> options,
            IHttpContextAccessor? httpContextAccessor = null,
            IAuditTrailEntryFactory? auditTrailEntryFactory = null) : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
            _auditTrailEntryFactory = auditTrailEntryFactory;
        }

        /// <summary>
        /// The tenant every query is scoped to, or <see cref="Guid.Empty"/> when no request is in
        /// flight (a hosted service, a worker, or the login path, which runs before any tenant is
        /// known).
        /// </summary>
        public Guid AmbientEnterpriseId
        {
            get
            {
                var user = _httpContextAccessor?.HttpContext?.User;

                if (user?.HasClaim(MachineClaims.ClientType, MachineClaims.SelfApiValue) == true
                    || user?.HasClaim(MachineClaims.TokenScope, MachineClaims.StorefrontScopeValue) == true)
                    return Guid.Empty;

                var claimValue = user?.FindFirst(ClaimTypes.GroupSid)?.Value;

                return Guid.TryParse(claimValue, out var enterpriseId) ? enterpriseId : Guid.Empty;
            }
        }

        public DbSet<BankStatementImport> BankStatementImport { get; set; }
        public DbSet<BankStatementLine> BankStatementLine { get; set; }
        public DbSet<Customer> Customer { get; set; }
        public DbSet<Employee> Employee { get; set; }
        public DbSet<EmployeeAbsence> EmployeeAbsence { get; set; }
        public DbSet<EmployeeVacationAccrual> EmployeeVacationAccrual { get; set; }
        public DbSet<Enterprise> Enterprise { get; set; }
        public DbSet<EnterprisePayrollSettings> EnterprisePayrollSettings { get; set; }
        public DbSet<EnterpriseFiscalSettings> EnterpriseFiscalSettings { get; set; }
        public DbSet<NFeNumberSequence> NFeNumberSequence { get; set; }
        public DbSet<Email> Email { get; set; }
        public DbSet<Bill> Bill { get; set; }
        public DbSet<DunningLog> DunningLog { get; set; }
        public DbSet<BillNumberSequence> BillNumberSequence { get; set; }
        public DbSet<NFeDocument> NFeDocument { get; set; }
        public DbSet<Order> Order { get; set; }
        public DbSet<OrderedProduct> OrderedProduct { get; set; }
        public DbSet<Product> Product { get; set; }
        public DbSet<StockMovement> StockMovement { get; set; }
        public DbSet<CashSession> CashSession { get; set; }
        public DbSet<CashMovement> CashMovement { get; set; }
        public DbSet<PdvSale> PdvSale { get; set; }
        public DbSet<StorefrontCheckout> StorefrontCheckout { get; set; }
        public DbSet<ProductVariantGroup> ProductVariantGroup { get; set; }
        public DbSet<StorefrontEvent> StorefrontEvent { get; set; }
        public DbSet<StorefrontCategory> StorefrontCategory { get; set; }
        public DbSet<StorefrontCategorySlugAlias> StorefrontCategorySlugAlias { get; set; }
        public DbSet<ProductPriceHistory> ProductPriceHistory { get; set; }
        public DbSet<PdvSalePayment> PdvSalePayment { get; set; }
        public DbSet<PdvTerminal> PdvTerminal { get; set; }
        public DbSet<PdvOverrideAudit> PdvOverrideAudit { get; set; }
        public DbSet<PdvCharge> PdvCharge { get; set; }
        public DbSet<Quote> Quote { get; set; }
        public DbSet<QuoteItem> QuoteItem { get; set; }
        public DbSet<QuoteNumberSequence> QuoteNumberSequence { get; set; }
        public DbSet<SalesGoal> SalesGoal { get; set; }
        public DbSet<PayableBill> PayableBill { get; set; }
        public DbSet<PayableBillProduct> PayableBillProduct { get; set; }
        public DbSet<PayrollBillDetail> PayrollBillDetail { get; set; }
        public DbSet<Supplier> Supplier { get; set; }
        public DbSet<Opportunity> Opportunity { get; set; }
        public DbSet<OpportunitySignal> OpportunitySignal { get; set; }
        public DbSet<OpportunityRecommendation> OpportunityRecommendation { get; set; }
        public DbSet<OpportunityFeedback> OpportunityFeedback { get; set; }
        public DbSet<OpportunityRunLog> OpportunityRunLog { get; set; }
        public DbSet<ForecastSimulationLog> ForecastSimulationLog { get; set; }
        public DbSet<AiConversation> AiConversation { get; set; }
        public DbSet<AiMessage> AiMessage { get; set; }
        public DbSet<AiPendingAction> AiPendingAction { get; set; }
        public DbSet<Notification> Notification { get; set; }
        public DbSet<NotificationRead> NotificationRead { get; set; }
        public DbSet<RefreshToken> RefreshToken { get; set; }
        public DbSet<PasswordResetToken> PasswordResetToken { get; set; }
        public DbSet<MfaRecoveryCode> MfaRecoveryCode { get; set; }
        public DbSet<User> User { get; set; }
        public DbSet<AuditTrail> AuditTrails { get; set; }
        public DbSet<ProcessedMessage> ProcessedMessage { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);

            configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Enterprise>()
                .Property(enterprise => enterprise.Currency)
                .HasMaxLength(3)
                .HasDefaultValue("BRL");

            modelBuilder.Entity<Enterprise>()
                .Property(enterprise => enterprise.CurrentBalance)
                .HasDefaultValue(0m);

            modelBuilder.Entity<Bill>()
                .ToTable("Boleto");

            modelBuilder.Entity<ProcessedMessage>()
                .HasIndex(processed => new { processed.Handler, processed.MessageId })
                .IsUnique();

            modelBuilder.Entity<ProcessedMessage>()
                .Property(processed => processed.MessageId)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<ProcessedMessage>()
                .Property(processed => processed.Handler)
                .HasMaxLength(64)
                .IsRequired();

            modelBuilder.Entity<ProcessedMessage>()
                .HasIndex(processed => processed.ProcessedAt);

            modelBuilder.Entity<Bill>()
                .HasIndex(bill => bill.OrderId)
                .HasFilter($"[Status] <> {(int)BillStatus.Cancelled}")
                .IsUnique();

            // Deliberately NOT filtered on IsActive — see BillNumberSequence. A soft-deleted counter
            // that let a replacement row restart at the range floor would re-issue NossoNumero
            // values already handed to payers.
            modelBuilder.Entity<BillNumberSequence>()
                .HasOne(sequence => sequence.Enterprise)
                .WithMany()
                .HasForeignKey(sequence => sequence.EnterpriseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DunningLog>()
                .HasOne(log => log.Enterprise)
                .WithMany()
                .HasForeignKey(log => log.EnterpriseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Notification>()
                .HasOne(notification => notification.CreatedByUser)
                .WithMany()
                .HasForeignKey(notification => notification.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<NotificationRead>()
                .HasOne(read => read.User)
                .WithMany()
                .HasForeignKey(read => read.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Email>()
                .HasOne(email => email.Enterprise)
                .WithMany()
                .HasForeignKey(email => email.EnterpriseId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<NFeDocument>()
                .Property(document => document.AccessKey)
                .HasMaxLength(44);

            modelBuilder.Entity<PayableBill>()
                .HasMany(payableBill => payableBill.Items)
                .WithOne(item => item.PayableBill)
                .HasForeignKey(item => item.PayableBillId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Quote>()
                .HasOne(quote => quote.Enterprise)
                .WithMany()
                .HasForeignKey(quote => quote.EnterpriseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Quote>()
                .HasOne(quote => quote.Customer)
                .WithMany()
                .HasForeignKey(quote => quote.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Quote>()
                .HasMany(quote => quote.Items)
                .WithOne(item => item.Quote)
                .HasForeignKey(item => item.QuoteId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QuoteItem>()
                .HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Quote>()
                .HasIndex(quote => quote.ConvertedOrderId)
                .HasFilter("[ConvertedOrderId] IS NOT NULL")
                .IsUnique();

            modelBuilder.Entity<Customer>()
                .HasIndex(customer => new { customer.EnterpriseId, customer.Latitude, customer.Longitude })
                .HasFilter("[Latitude] IS NOT NULL AND [Longitude] IS NOT NULL");

            modelBuilder.Entity<SalesGoal>()
                .HasOne(goal => goal.Enterprise)
                .WithMany()
                .HasForeignKey(goal => goal.EnterpriseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SalesGoal>()
                .HasOne(goal => goal.User)
                .WithMany()
                .HasForeignKey(goal => goal.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // One live target per seller per competência, the enterprise-wide row (UserId null)
            // included — SQL Server treats nulls as equal in a unique index, which is exactly the
            // behaviour wanted here. Filtered on IsActive so a target that was deleted and set
            // again does not collide with its own tombstone.
            // The monthly sales aggregate filters a tenant's orders by CreatedAt, and Order carried
            // only a single-column index on EnterpriseId - so every run seeked the tenant and then
            // looked up and discarded most of what it read. This is the covering shape for that
            // query.
            //
            // Deliberately NOT filtered on IsActive: the column is nullable and legacy rows carry
            // NULL, which SQL Server excludes from an "[IsActive] = 1" filter - the same trap noted
            // on the Bill index above.
            modelBuilder.Entity<Order>()
                .HasIndex(order => new { order.EnterpriseId, order.IsActive, order.CreatedAt })
                .IncludeProperties(order => new
                {
                    order.UserId,
                    order.Status,
                    order.RefundedAt,
                    order.Payday,
                    order.TotalValue
                });

            modelBuilder.Entity<StorefrontCategory>()
                .HasOne(category => category.Parent)
                .WithMany()
                .HasForeignKey(category => category.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StorefrontCategory>().HasData(StorefrontCategorySeed.Categories());

            modelBuilder.Entity<Product>()
                .HasOne(product => product.StorefrontCategory)
                .WithMany()
                .HasForeignKey(product => product.StorefrontCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Product>()
                .HasIndex(product => new { product.StorefrontCategoryId, product.IsPublishedOnStorefront });

            modelBuilder.Entity<Order>()
                .HasIndex(order => new { order.EnterpriseId, order.ExternalReference })
                .HasFilter("[ExternalReference] IS NOT NULL")
                .IsUnique();

            modelBuilder.Entity<Order>()
                .HasIndex(order => order.ExternalReference)
                .HasFilter("[ExternalReference] IS NOT NULL");

            // ponytail: not unique — two first purchases racing may create a second customer row for
            // the same shopper; harmless, and a unique index would fail one of the orders instead.
            modelBuilder.Entity<Customer>()
                .HasIndex(customer => new { customer.EnterpriseId, customer.ExternalShopperId })
                .HasFilter("[ExternalShopperId] IS NOT NULL");

            modelBuilder.Entity<Bill>()
                .HasIndex(bill => new { bill.OrderId, bill.IsActive, bill.Status })
                .IncludeProperties(bill => new { bill.CreatedAt, bill.Amount, bill.PaidAmount });

            modelBuilder.Entity<SalesGoal>()
                .HasIndex(goal => new { goal.EnterpriseId, goal.UserId, goal.Year, goal.Month })
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            modelBuilder.Entity<PayableBillProduct>()
                .HasOne(item => item.Product)
                .WithMany(product => product.PayableBillProducts)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PayableBillProduct>()
                .HasIndex(item => new { item.PayableBillId, item.ProductId })
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            modelBuilder.Entity<Product>()
                .HasIndex(product => new { product.VariantGroupId, product.VariantLabel })
                .HasFilter("[IsActive] = 1 AND [VariantGroupId] IS NOT NULL")
                .IsUnique();

            modelBuilder.Entity<Enterprise>()
                .HasIndex(enterprise => enterprise.StorefrontSlug)
                .HasFilter("[StorefrontSlug] IS NOT NULL")
                .IsUnique();

            modelBuilder.Entity<Product>()
                .HasIndex(product => new { product.IsPublishedOnStorefront, product.EnterpriseId });

            modelBuilder.Entity<StockMovement>()
                .HasOne(movement => movement.Product)
                .WithMany()
                .HasForeignKey(movement => movement.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<BankStatementLine>()
                .HasOne(line => line.Import)
                .WithMany()
                .HasForeignKey(line => line.ImportId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PayrollBillDetail>()
                .HasOne(detail => detail.Employee)
                .WithMany()
                .HasForeignKey(detail => detail.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<EmployeeAbsence>()
                .HasOne(absence => absence.Employee)
                .WithMany()
                .HasForeignKey(absence => absence.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<EmployeeVacationAccrual>()
                .HasOne(accrual => accrual.Employee)
                .WithMany()
                .HasForeignKey(accrual => accrual.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PayableBill>()
                .HasOne(bill => bill.EmployeeVacationAccrual)
                .WithMany()
                .HasForeignKey(bill => bill.EmployeeVacationAccrualId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PayableBill>()
                .HasIndex(bill => bill.EmployeeVacationAccrualId)
                .HasFilter("[EmployeeVacationAccrualId] IS NOT NULL");

            modelBuilder.Entity<PayableBill>()
                .HasIndex(bill => new { bill.EnterpriseId, bill.SupplierNFeAccessKey, bill.SupplierNFeDuplicata })
                .HasFilter("[SupplierNFeAccessKey] IS NOT NULL")
                .IsUnique();

            modelBuilder.Entity<PayableBill>()
                .HasIndex(bill => bill.RecurrenceParentId)
                .HasFilter("[RecurrenceParentId] IS NOT NULL")
                .IsUnique();

            modelBuilder.Entity<PayrollBillDetail>()
                .HasIndex(detail => new { detail.ReferenceYear, detail.ReferenceMonth, detail.EmployeeId })
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            modelBuilder.Entity<EnterprisePayrollSettings>()
                .HasIndex(settings => settings.EnterpriseId)
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            modelBuilder.Entity<EnterpriseFiscalSettings>()
                .HasIndex(settings => settings.EnterpriseId)
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            // Deliberately NOT filtered on IsActive — see NFeNumberSequence. A soft-deleted
            // counter that let a replacement row restart at 1 would re-issue nNF values already
            // transmitted to SEFAZ.
            modelBuilder.Entity<NFeNumberSequence>()
                .HasOne(sequence => sequence.Enterprise)
                .WithMany()
                .HasForeignKey(sequence => sequence.EnterpriseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PasswordResetToken>()
                .HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(token => token.UserId)
                .HasDatabaseName("IX_PasswordResetToken_UserId_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            modelBuilder.Entity<User>()
                .HasIndex(user => user.AppleSubject)
                .IsUnique()
                .HasFilter("[AppleSubject] IS NOT NULL");

            modelBuilder.Entity<MfaRecoveryCode>()
                .HasOne(code => code.User)
                .WithMany()
                .HasForeignKey(code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(token => token.UserId)
                .HasDatabaseName("IX_RefreshToken_UserId_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            modelBuilder.Entity<User>()
                .Property<string>("NormalizedActiveUserName")
                .HasMaxLength(450)
                .HasComputedColumnSql(
                    "CASE WHEN [IsActive] = 1 THEN CONVERT(nvarchar(450), UPPER(LTRIM(RTRIM([UserName])))) END",
                    stored: true);

            modelBuilder.Entity<User>()
                .Property<string>("NormalizedActiveEmail")
                .HasMaxLength(450)
                .HasComputedColumnSql(
                    "CASE WHEN [IsActive] = 1 THEN CONVERT(nvarchar(450), UPPER(LTRIM(RTRIM([Email])))) END",
                    stored: true);

            modelBuilder.Entity<User>()
                .HasIndex("EnterpriseId", "NormalizedActiveUserName")
                .HasDatabaseName("IX_User_EnterpriseId_NormalizedActiveUserName_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1 AND [EnterpriseId] IS NOT NULL");

            modelBuilder.Entity<User>()
                .HasIndex("NormalizedActiveEmail")
                .HasDatabaseName("IX_User_NormalizedActiveEmail_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.Type)
                .HasMaxLength(64)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.Status)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.Title)
                .HasMaxLength(256)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.SourceRule)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.SourceModel)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.Fingerprint)
                .HasMaxLength(256)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .HasMany(opportunity => opportunity.Signals)
                .WithOne(signal => signal.Opportunity)
                .HasForeignKey(signal => signal.OpportunityId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Opportunity>()
                .HasMany(opportunity => opportunity.Recommendations)
                .WithOne(recommendation => recommendation.Opportunity)
                .HasForeignKey(recommendation => recommendation.OpportunityId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Opportunity>()
                .HasMany(opportunity => opportunity.Feedbacks)
                .WithOne(feedback => feedback.Opportunity)
                .HasForeignKey(feedback => feedback.OpportunityId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AiConversation>()
                .HasMany(conversation => conversation.Messages)
                .WithOne(message => message.AiConversation)
                .HasForeignKey(message => message.AiConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AiPendingAction>()
                .HasOne(action => action.AiConversation)
                .WithMany()
                .HasForeignKey(action => action.AiConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.SignalType)
                .HasMaxLength(64)
                .IsRequired();

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.SignalKey)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.Unit)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.SourceEntity)
                .HasMaxLength(64)
                .IsRequired();

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.SourceEntityId)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<OpportunityRecommendation>()
                .Property(recommendation => recommendation.ActionTitle)
                .HasMaxLength(160)
                .IsRequired();

            modelBuilder.Entity<OpportunityRecommendation>()
                .Property(recommendation => recommendation.PriorityLabel)
                .HasMaxLength(16)
                .IsRequired();

            modelBuilder.Entity<OpportunityFeedback>()
                .Property(feedback => feedback.Status)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<OpportunityFeedback>()
                .HasOne(feedback => feedback.User)
                .WithMany()
                .HasForeignKey(feedback => feedback.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<OpportunityRunLog>()
                .Property(runLog => runLog.TriggerType)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<OpportunityRunLog>()
                .Property(runLog => runLog.Status)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<OpportunityRunLog>()
                .Property(runLog => runLog.CorrelationId)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<OpportunityRunLog>()
                .HasOne(runLog => runLog.User)
                .WithMany()
                .HasForeignKey(runLog => runLog.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AuditTrail>()
                .Property(audit => audit.Action)
                .HasMaxLength(16)
                .IsRequired();

            modelBuilder.Entity<AuditTrail>()
                .Property(audit => audit.EntityName)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<AuditTrail>()
                .Property(audit => audit.ChangesJson)
                .IsRequired();

            modelBuilder.Entity<AuditTrail>()
                .HasOne(audit => audit.User)
                .WithMany()
                .HasForeignKey(audit => audit.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CashSession>()
                .HasIndex(session => new { session.EnterpriseId, session.TerminalId })
                .HasFilter($"[Status] = {(int)EnumCashSessionStatus.Open}")
                .IsUnique();

            modelBuilder.Entity<CashSession>()
                .HasOne(session => session.User)
                .WithMany()
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CashMovement>()
                .HasOne(movement => movement.CashSession)
                .WithMany(session => session.Movements)
                .HasForeignKey(movement => movement.CashSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PdvSale>()
                .HasOne(sale => sale.Order)
                .WithMany()
                .HasForeignKey(sale => sale.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PdvSale>()
                .HasOne(sale => sale.CashSession)
                .WithMany()
                .HasForeignKey(sale => sale.CashSessionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PdvSalePayment>()
                .HasOne(payment => payment.PdvSale)
                .WithMany(sale => sale.Payments)
                .HasForeignKey(payment => payment.PdvSaleId)
                .OnDelete(DeleteBehavior.Cascade);

            ApplyTenantQueryFilters(modelBuilder);
            ApplyInheritedTenantQueryFilters(modelBuilder);
        }

        /// <summary>
        /// Scopes every tenant-owned entity to <see cref="AmbientEnterpriseId"/> in the model itself.
        /// </summary>
        private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (entityType.BaseType != null || entityType.ClrType == null)
                    continue;

                var tenantProperty = entityType.ClrType == typeof(Enterprise)
                    ? nameof(BaseEntity.Id)
                    : entityType.ClrType.GetProperty(TenantPropertyName) == null ? null : TenantPropertyName;

                if (tenantProperty == null)
                    continue;

                ApplyTenantQueryFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder, tenantProperty]);
            }
        }

        /// <summary>
        /// Scopes the entities that carry no <c>EnterpriseId</c> of their own through the required
        /// parent that does. Without this they are the unfiltered end of a filtered relationship:
        /// <c>Set&lt;Bill&gt;()</c> would return every tenant's boletos unless the caller remembered
        /// to correlate it to an order.
        /// <para>
        /// Note the ceiling: a filter that reads through a required navigation expands to an inner
        /// join, so a row whose parent is missing disappears even when
        /// <see cref="AmbientEnterpriseId"/> is <see cref="Guid.Empty"/> and the filter is nominally
        /// off. The database's foreign keys guarantee the parent exists; an in-memory fixture that
        /// seeds children without parents does not, and will see the children vanish.
        /// </para>
        /// </summary>
        private void ApplyInheritedTenantQueryFilters(ModelBuilder modelBuilder)
        {

            modelBuilder.Entity<Bill>().HasQueryFilter(bill =>
                AmbientEnterpriseId == Guid.Empty || bill.Order!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<RefreshToken>().HasQueryFilter(token =>
                AmbientEnterpriseId == Guid.Empty || token.User!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<PasswordResetToken>().HasQueryFilter(token =>
                AmbientEnterpriseId == Guid.Empty || token.User!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<MfaRecoveryCode>().HasQueryFilter(code =>
                AmbientEnterpriseId == Guid.Empty || code.User!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<OrderedProduct>().HasQueryFilter(line =>
                AmbientEnterpriseId == Guid.Empty || line.Product!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<QuoteItem>().HasQueryFilter(item =>
                AmbientEnterpriseId == Guid.Empty || item.Quote!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<PayableBillProduct>().HasQueryFilter(item =>
                AmbientEnterpriseId == Guid.Empty || item.PayableBill!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<OpportunitySignal>().HasQueryFilter(signal =>
                AmbientEnterpriseId == Guid.Empty || signal.Opportunity!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<OpportunityRecommendation>().HasQueryFilter(recommendation =>
                AmbientEnterpriseId == Guid.Empty || recommendation.Opportunity!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<OpportunityFeedback>().HasQueryFilter(feedback =>
                AmbientEnterpriseId == Guid.Empty || feedback.Opportunity!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<AiMessage>().HasQueryFilter(message =>
                AmbientEnterpriseId == Guid.Empty || message.AiConversation!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<AiPendingAction>().HasQueryFilter(action =>
                AmbientEnterpriseId == Guid.Empty || action.AiConversation!.EnterpriseId == AmbientEnterpriseId);

            modelBuilder.Entity<NotificationRead>().HasQueryFilter(read =>
                AmbientEnterpriseId == Guid.Empty || read.Notification!.EnterpriseId == AmbientEnterpriseId);
        }

        private const string TenantPropertyName = "EnterpriseId";

        private static readonly System.Reflection.MethodInfo ApplyTenantQueryFilterMethod =
            typeof(AppDbContext).GetMethod(
                nameof(ApplyTenantQueryFilter),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        /// <summary>
        /// The filter itself, generic so the lambda is a real typed expression rather than a
        /// hand-assembled tree. <c>EF.Property</c> reads the column whether it is declared
        /// <c>Guid</c> or <c>Guid?</c>, which the entities are inconsistent about.
        /// </summary>
        private void ApplyTenantQueryFilter<TEntity>(ModelBuilder modelBuilder, string tenantProperty)
            where TEntity : class
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
                AmbientEnterpriseId == Guid.Empty
                || EF.Property<Guid?>(entity, tenantProperty) == AmbientEnterpriseId);
        }

        public override int SaveChanges()
        {
            return SaveChanges(acceptAllChangesOnSuccess: true);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            RestoreOriginalValuesForAudit();
            AppendAuditTrails();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            await RestoreOriginalValuesForAuditAsync(cancellationToken);
            AppendAuditTrails();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        /// <summary>
        /// Reloads database original values for audited entities that were attached from a detached
        /// instance, so the audit trail can tell what actually changed.
        /// </summary>
        private async Task RestoreOriginalValuesForAuditAsync(CancellationToken cancellationToken)
        {
            foreach (var entry in CollectAuditedEntriesNeedingOriginalValues())
            {
                var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
                if (databaseValues != null)
                    entry.OriginalValues.SetValues(databaseValues);
            }
        }

        private void RestoreOriginalValuesForAudit()
        {
            foreach (var entry in CollectAuditedEntriesNeedingOriginalValues())
            {
                var databaseValues = entry.GetDatabaseValues();
                if (databaseValues != null)
                    entry.OriginalValues.SetValues(databaseValues);
            }
        }

        private List<EntityEntry<BaseEntity>> CollectAuditedEntriesNeedingOriginalValues()
        {
            ChangeTracker.DetectChanges();

            return ChangeTracker.Entries<BaseEntity>()
                .Where(entry => entry.State == EntityState.Modified
                                && AuditedEntities.Contains(entry.Metadata.ClrType)
                                && entry.Properties.All(property =>
                                    !property.IsModified || Equals(property.OriginalValue, property.CurrentValue)))
                .ToList();
        }

        private void AppendAuditTrails()
        {
            var now = DateTime.UtcNow;

            ChangeTracker.DetectChanges();

            var auditableEntries = ChangeTracker.Entries<BaseEntity>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();

            if (auditableEntries.Count == 0)
                return;

            var userId = ResolveCurrentUserId();

            if (_auditTrailEntryFactory == null)
                throw new InvalidOperationException("IAuditTrailEntryFactory is not registered. Configure AuditTrailIoC before using AppDbContext.");

            var enterpriseId = ResolveCurrentEnterpriseId();
            var pendingAuditEntries = _auditTrailEntryFactory.Create(auditableEntries, userId, enterpriseId, now);

            if (pendingAuditEntries.Count == 0)
                return;

            AuditTrails.AddRange(pendingAuditEntries);
        }

        private Guid? ResolveCurrentUserId()
        {
            var user = _httpContextAccessor?.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            var idClaim = user.FindFirst(ClaimTypes.Sid)?.Value
                          ?? user.FindFirst("uid")?.Value
                          ?? user.FindFirst("sub")?.Value;

            if (!string.IsNullOrWhiteSpace(idClaim) && Guid.TryParse(idClaim, out var parsedId))
                return parsedId;

            var userName = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? user.Identity?.Name;

            if (string.IsNullOrWhiteSpace(userName))
                return null;

            var trackedUserId = ChangeTracker.Entries<User>()
                .Where(entry => entry.State is not EntityState.Deleted)
                .Select(entry => entry.Entity)
                .Where(entity => entity.UserName == userName)
                .Select(entity => (Guid?)entity.Id)
                .FirstOrDefault();

            if (trackedUserId.HasValue)
                return trackedUserId.Value;

            return User.AsNoTracking()
                .Where(entity => entity.UserName == userName)
                .Select(entity => (Guid?)entity.Id)
                .FirstOrDefault();
        }

        private Guid? ResolveCurrentEnterpriseId()
        {
            var user = _httpContextAccessor?.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            var enterpriseClaim = user.FindFirst(ClaimTypes.GroupSid)?.Value
                                  ?? user.FindFirst("enterprise_id")?.Value
                                  ?? user.FindFirst("tenant_id")?.Value;

            return !string.IsNullOrWhiteSpace(enterpriseClaim) &&
                   Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
                   enterpriseId != Guid.Empty
                ? enterpriseId
                : null;
        }
    }
}
