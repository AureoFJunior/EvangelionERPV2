using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace EvangelionERPV2.Domain.Models
{
    [Index(nameof(Id), nameof(CreatedAt), nameof(UpdatedAt))]
    [Index(nameof(Id), nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive))]
    public abstract class BaseEntity
    {
        [Key]
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.MinValue;
        public bool? IsActive { get; set; } = true;
    }
}