using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace EvangelionERPV2.AuditModule.Application.Services
{
    public class AuditTrailEntryFactory : IAuditTrailEntryFactory
    {
        private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Password",
            "Token",
            "TokenHash",
            "RefreshToken",
            "AccessKey",
            "XmlContent",
            "CancelReason",
            "CancelProtocol",
            "Series",
            "Number",
            "Email",
            "PhoneNumber",
            "Document",
            "Adress",
            "Address",
            "FirstName",
            "LastName",
            "UserName",
            "BirthDate",
            "ProfilePicture",
            "RefundReason",
            "HtmlContent"
        };

        public IReadOnlyCollection<AuditTrail> Create(
            IEnumerable<EntityEntry<BaseEntity>> entries,
            Guid? userId,
            Guid? enterpriseId,
            DateTime changedAt)
        {
            var result = new List<AuditTrail>();

            foreach (var entry in entries)
            {
                if (!AuditedEntities.Contains(entry.Metadata.ClrType))
                    continue;

                var auditEntry = BuildAuditEntry(entry, userId, enterpriseId, changedAt);
                if (auditEntry != null)
                    result.Add(auditEntry);
            }

            return result;
        }

        private static AuditTrail? BuildAuditEntry(
            EntityEntry<BaseEntity> entry,
            Guid? userId,
            Guid? enterpriseId,
            DateTime changedAt)
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
                            NewValue = RedactValue(entry.Metadata.ClrType, property.Metadata.Name, property.CurrentValue)
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
                            OldValue = RedactValue(entry.Metadata.ClrType, property.Metadata.Name, property.OriginalValue),
                            NewValue = RedactValue(entry.Metadata.ClrType, property.Metadata.Name, property.CurrentValue)
                        };
                    }
                    break;
                case EntityState.Deleted:
                    action = "DELETE";
                    foreach (var property in entry.Properties.Where(property => !property.Metadata.IsShadowProperty()))
                    {
                        changeSet[property.Metadata.Name] = new AuditPropertyChange
                        {
                            OldValue = RedactValue(entry.Metadata.ClrType, property.Metadata.Name, property.OriginalValue)
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
                EnterpriseId = ResolveEnterpriseId(entry) ?? enterpriseId,
                ChangedAt = changedAt,
                Action = action,
                EntityName = entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.Id,
                ChangesJson = JsonSerializer.Serialize(changeSet)
            };
        }

        private static Guid? ResolveEnterpriseId(EntityEntry<BaseEntity> entry)
        {
            var enterpriseProperty = entry.Properties
                .FirstOrDefault(property => property.Metadata.Name == "EnterpriseId");

            var value = enterpriseProperty?.CurrentValue ?? enterpriseProperty?.OriginalValue;
            return value is Guid enterpriseId && enterpriseId != Guid.Empty
                ? enterpriseId
                : null;
        }

        private static object? RedactValue(Type entityType, string propertyName, object? value)
        {
            if (!ShouldRedact(entityType, propertyName))
                return value;

            if (value == null)
                return null;

            return "[redacted]";
        }

        private static bool ShouldRedact(Type entityType, string propertyName)
        {
            if (SensitivePropertyNames.Contains(propertyName))
                return true;

            if (entityType == typeof(Customer))
            {
                return propertyName.Equals(nameof(Customer.Name), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(Customer.Email), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(Customer.PhoneNumber), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(Customer.Adress), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(Customer.Document), StringComparison.OrdinalIgnoreCase);
            }

            if (entityType == typeof(User))
            {
                return propertyName.Equals(nameof(User.FirstName), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(User.LastName), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(User.UserName), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(User.Email), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(User.BirthDate), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(User.ProfilePicture), StringComparison.OrdinalIgnoreCase);
            }

            if (entityType == typeof(Order))
            {
                return propertyName.Equals(nameof(Order.RefundReason), StringComparison.OrdinalIgnoreCase);
            }

            if (entityType == typeof(Bill))
            {
                return propertyName.Equals(nameof(Bill.HtmlContent), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(Bill.DocumentNumber), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(Bill.DigitableLine), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(Bill.BarCode), StringComparison.OrdinalIgnoreCase)
                    || propertyName.Equals(nameof(Bill.OurNumber), StringComparison.OrdinalIgnoreCase);
            }

            if (entityType == typeof(PayableBill))
            {
                return propertyName.Equals(nameof(PayableBill.RefundReason), StringComparison.OrdinalIgnoreCase);
            }

            return false;
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
