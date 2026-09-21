using EvangelionERPV2.AuditModule.Application.Services;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The CID on an atestado is LGPD art. 11 special-category health data. Three doors it must
    /// never leave by: the API response, the audit trail, and — covered by the module tests —
    /// the database in the clear.
    /// </summary>
    public class EmployeeAbsenceCidLgpdTests
    {
        private const string Cid = "M54.5";

        [Fact]
        public void EmployeeAbsenceDTO_HasNoCidPropertyAtAll()
        {
            Assert.Null(typeof(EmployeeAbsenceDTO).GetProperty("Cid"));
        }

        [Fact]
        public void EmployeeAbsenceDTO_ListSerialization_NeverContainsTheCid()
        {
            var absences = new List<EmployeeAbsenceDTO>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = Guid.NewGuid(),
                    Type = (int)EnumEmployeeAbsenceType.Atestado,
                    StartDate = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
                    EndDate = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
                    IsJustified = true,
                    Reason = "Atestado apresentado",
                    EnterpriseId = Guid.NewGuid()
                }
            };

            var json = JsonSerializer.Serialize(absences);

            Assert.DoesNotContain(Cid, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cid", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("IsJustified", json);
        }

        [Fact]
        public void AuditTrail_ForAnAtestado_RedactsTheCidAndTheReason()
        {
            using var context = CreateContext();
            var absence = new EmployeeAbsence
            {
                Id = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid(),
                Type = (int)EnumEmployeeAbsenceType.Atestado,
                StartDate = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
                IsJustified = true,
                Reason = "lombalgia recorrente",
                Cid = Cid,
                EnterpriseId = Guid.NewGuid(),
                IsActive = true
            };

            var entry = context.Entry<BaseEntity>(absence);
            entry.State = EntityState.Added;

            var trail = Assert.Single(new AuditTrailEntryFactory().Create(
                [entry],
                userId: Guid.NewGuid(),
                enterpriseId: absence.EnterpriseId,
                changedAt: DateTime.UtcNow));

            Assert.DoesNotContain(Cid, trail.ChangesJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lombalgia", trail.ChangesJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[redacted]", trail.ChangesJson);
            Assert.Contains("IsJustified", trail.ChangesJson);
        }

        private static AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"absence-audit-{Guid.NewGuid()}")
                .Options;

            return new AppDbContext(options);
        }
    }
}
