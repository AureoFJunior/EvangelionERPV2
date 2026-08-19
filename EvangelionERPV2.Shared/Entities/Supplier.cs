using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(Name))]
    [Index(nameof(Name))]
    public class Supplier : BaseEntity
    {
        public Supplier() { }

        public Supplier(string name, string phoneNumber, string email, string adress, string? document = null)
        {
            Name = name;
            PhoneNumber = phoneNumber;
            Email = email;
            Adress = adress;
            Document = document;
        }

        public string Name { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Adress { get; set; } = "";

        // LGPD: CNPJ/CPF — never serialized to API responses (see SupplierDTO).
        public string? Document { get; set; } = null;

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;
        public virtual Enterprise? Enterprise { get; set; } = null;
    }
}
