using System.ComponentModel.DataAnnotations;

namespace EvangelionERPV2.Domain.Models
{
    public abstract class BaseEntity
    {
        [Key]
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.MinValue;
        public bool? IsActive { get; set; } = true;
    }
}