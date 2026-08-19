using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(Name))]
    [Index(nameof(Name))]
    public class Employee : BaseEntity
    {
        public Employee() { }

        public Employee(string name, string phoneNumber, string email, string position, decimal? salary = null, string? document = null, DateTime? hireDate = null)
        {
            Name = name;
            PhoneNumber = phoneNumber;
            Email = email;
            Position = position;
            Salary = salary;
            Document = document;
            HireDate = hireDate ?? DateTime.UtcNow;
        }

        public string Name { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Position { get; set; } = "";

        // Optional. Stores the encrypted S3 object key, never the image itself
        // (same storage pattern as User.ProfilePicture / Product.PictureAdress).
        public string? Picture { get; set; } = null;

        // LGPD: CPF/identity document — never serialized to API responses (see EmployeeDTO).
        public string? Document { get; set; } = null;

        // LGPD: financial data — never serialized to API responses (see EmployeeDTO).
        [Column(TypeName = "decimal(18,2)")]
        public decimal? Salary { get; set; } = null;

        public DateTime HireDate { get; set; } = DateTime.UtcNow;
        public DateTime? TerminationDate { get; set; } = null;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;
        public virtual Enterprise? Enterprise { get; set; } = null;
    }
}
