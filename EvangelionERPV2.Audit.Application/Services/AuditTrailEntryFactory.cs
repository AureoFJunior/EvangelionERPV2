using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace EvangelionERPV2.AuditModule.Application.Services
{
    public class AuditTrailEntryFactory : IAuditTrailEntryFactory
    {
        public IReadOnlyCollection<AuditTrail> Create(
            IEnumerable<EntityEntry<BaseEntity>> entries,
            Guid? userId,
            DateTime changedAt)
        {
            var result = new List<AuditTrail>();

            foreach (var entry in entries)
            {
                if (!AuditedEntities.Contains(entry.Metadata.ClrType))
                    continue;

                var auditEntry = BuildAuditEntry(entry, userId, changedAt);
                if (auditEntry != null)
                    result.Add(auditEntry);
            }

            return result;
        }

        private static AuditTrail? BuildAuditEntry(EntityEntry<BaseEntity> entry, Guid? userId, DateTime changedAt)
        {
            var changeSet = new Dictionary<string, AuditPropertyChange>();
            string action;

            switch (entry.State)
            {
                case EntityState.Added:
                    action = "ADD";
                    foreach (var property in entry.Properties.Where(property => !property.Metadata.IsShadowProperty()))
                    {
                        changeSet[property.Metadata.Name] = new AuditPropertyChange
                        {
                            NewValue = property.CurrentValue
                        };
                    }
                    break;
                case EntityState.Modified:
                    action = IsSoftDelete(entry) ? "DELETE" : "EDIT";
                    foreach (var property in entry.Properties.Where(property => property.IsModified && !property.Metadata.IsShadowProperty()))
                    {
                        if (Equals(property.OriginalValue, property.CurrentValue))
                            continue;

                        changeSet[property.Metadata.Name] = new AuditPropertyChange
                        {
                            OldValue = property.OriginalValue,
                            NewValue = property.CurrentValue
                        };
                    }
                    break;
                case EntityState.Deleted:
                    action = "DELETE";
                    foreach (var property in entry.Properties.Where(property => !property.Metadata.IsShadowProperty()))
                    {
                        changeSet[property.Metadata.Name] = new AuditPropertyChange
                        {
                            OldValue = property.OriginalValue
                        };
                    }
                    break;
                default:
                    return null;
            }

            if (changeSet.Count == 0)
                return null;

            return new AuditTrail
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ChangedAt = changedAt,
                Action = action,
                EntityName = entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.Id,
                ChangesJson = JsonSerializer.Serialize(changeSet)
            };
        }

        private static bool IsSoftDelete(EntityEntry<BaseEntity> entry)
        {
            var isActiveProperty = entry.Properties
                .FirstOrDefault(property => property.Metadata.Name == nameof(BaseEntity.IsActive));

            if (isActiveProperty == null || !isActiveProperty.IsModified)
                return false;

            var originalValue = ToNullableBool(isActiveProperty.OriginalValue);
            var currentValue = ToNullableBool(isActiveProperty.CurrentValue);

            return currentValue == false && originalValue != false;
        }

        private static bool? ToNullableBool(object? value)
        {
            return value switch
            {
                null => null,
                bool boolValue => boolValue,
                _ => null
            };
        }

        private sealed class AuditPropertyChange
        {
            public object? OldValue { get; set; }
            public object? NewValue { get; set; }
        }
    }
}
