using EvangelionERPV2.AuditModule.Application.Services;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Every read in <c>RepositoryBase</c> is <c>AsNoTracking</c>, so the update flow everywhere in
    /// this solution is read-detached → mutate → <c>Update</c>. That attaches the entity with
    /// OriginalValues copied from the instance being saved, which made the audit change set empty and
    /// suppressed the EDIT row entirely — for every audited entity, not only fiscal ones.
    /// These tests pin the trail down, since nothing else asserted an EDIT row existed.
    /// </summary>
    public class AuditTrailEditRowTests
    {
        [Fact]
        public async Task SaveChangesAsync_DetachedUpdateOfAuditedEntity_WritesEditRowWithRealOldValue()
        {
            var databaseName = $"audit-edit-{Guid.NewGuid():N}";
            var documentId = Guid.NewGuid();

            await using (var seed = CreateContext(databaseName))
            {
                seed.Add(NewDocument(documentId, NFeStatus.Authorized));
                await seed.SaveChangesAsync();
            }

            await using (var act = CreateContext(databaseName))
            {
                var detached = await act.Set<NFeDocument>().AsNoTracking()
                    .FirstAsync(x => x.Id == documentId);

                detached.Status = NFeStatus.Cancelled;
                act.Set<NFeDocument>().Update(detached);
                await act.SaveChangesAsync();
            }

            await using var assertContext = CreateContext(databaseName);
            var edit = Assert.Single(
                await assertContext.AuditTrails.AsNoTracking()
                    .Where(x => x.EntityId == documentId && x.Action == "EDIT")
                    .ToListAsync());

            var changes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(edit.ChangesJson)!;

            Assert.True(changes.ContainsKey(nameof(NFeDocument.Status)),
                "the EDIT row must record the Status transition that was actually made");

            var statusChange = changes[nameof(NFeDocument.Status)];
            Assert.Equal(
                ((int)NFeStatus.Authorized).ToString(),
                statusChange.GetProperty("OldValue").ToString());
            Assert.Equal(
                ((int)NFeStatus.Cancelled).ToString(),
                statusChange.GetProperty("NewValue").ToString());
        }

        [Fact]
        public async Task SaveChangesAsync_DetachedUpdateThatChangesNothing_WritesNoEditRow()
        {
            var databaseName = $"audit-noop-{Guid.NewGuid():N}";
            var documentId = Guid.NewGuid();

            await using (var seed = CreateContext(databaseName))
            {
                seed.Add(NewDocument(documentId, NFeStatus.Authorized));
                await seed.SaveChangesAsync();
            }

            await using (var act = CreateContext(databaseName))
            {
                var detached = await act.Set<NFeDocument>().AsNoTracking()
                    .FirstAsync(x => x.Id == documentId);

                act.Set<NFeDocument>().Update(detached);
                await act.SaveChangesAsync();
            }

            await using var assertContext = CreateContext(databaseName);
            var edits = await assertContext.AuditTrails.AsNoTracking()
                .CountAsync(x => x.EntityId == documentId && x.Action == "EDIT");

            Assert.Equal(0, edits);
        }

        private static NFeDocument NewDocument(Guid id, NFeStatus status) => new()
        {
            Id = id,
            EnterpriseId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            Status = status,
            Type = NFeDocumentType.NFe,
            Model = 55,
        };

        private static AppDbContext CreateContext(string databaseName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;

            return new AppDbContext(options, httpContextAccessor: null, new AuditTrailEntryFactory());
        }
    }
}
