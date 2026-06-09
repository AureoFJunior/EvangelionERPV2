using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

        public DbSet<Customer> Customer { get; set; }
        public DbSet<Enterprise> Enterprise { get; set; }
        public DbSet<Email> Email { get; set; }
        public DbSet<Bill> Bill { get; set; }
        public DbSet<NFeDocument> NFeDocument { get; set; }
        public DbSet<Order> Order { get; set; }
        public DbSet<OrderedProduct> OrderedProduct { get; set; }
        public DbSet<Product> Product { get; set; }
        public DbSet<PayableBill> PayableBill { get; set; }
        public DbSet<PayableBillProduct> PayableBillProduct { get; set; }
        public DbSet<Opportunity> Opportunity { get; set; }
        public DbSet<OpportunitySignal> OpportunitySignal { get; set; }
        public DbSet<OpportunityRecommendation> OpportunityRecommendation { get; set; }
        public DbSet<OpportunityFeedback> OpportunityFeedback { get; set; }
        public DbSet<OpportunityRunLog> OpportunityRunLog { get; set; }
        public DbSet<ForecastSimulationLog> ForecastSimulationLog { get; set; }
        public DbSet<RefreshToken> RefreshToken { get; set; }
        public DbSet<PasswordResetToken> PasswordResetToken { get; set; }
        public DbSet<User> User { get; set; }
        public DbSet<AuditTrail> AuditTrails { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Enterprise>()
                .Property(enterprise => enterprise.Currency)
                .HasMaxLength(3)
                .HasDefaultValue("BRL");

            modelBuilder.Entity<Enterprise>()
                .Property(enterprise => enterprise.CurrentBalance)
                .HasDefaultValue(0d);

            modelBuilder.Entity<Bill>()
                .ToTable("Boleto");

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

            modelBuilder.Entity<PayableBillProduct>()
                .HasOne(item => item.Product)
                .WithMany(product => product.PayableBillProducts)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PayableBillProduct>()
                .HasIndex(item => new { item.PayableBillId, item.ProductId })
                .HasFilter("[IsActive] = 1")
                .IsUnique();

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
                .HasFilter("[IsActive] = 1 AND [NormalizedActiveEmail] IS NOT NULL");

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
        }

        public override int SaveChanges()
        {
            return SaveChanges(acceptAllChangesOnSuccess: true);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            AppendAuditTrails();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            AppendAuditTrails();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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
