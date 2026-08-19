using System.ComponentModel.DataAnnotations;

namespace EvangelionERPV2.Shared.Entities
{
    // No index attributes here on purpose: Id is already the (clustered) primary key,
    // so any index leading with Id only duplicates it and adds write cost on every table.
    // Index real filter columns (EnterpriseId, DueDate, IsActive) on the entity that needs them.
    public abstract class BaseEntity
    {
        [Key]
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.MinValue;
        public bool? IsActive { get; set; } = true;
    }
}