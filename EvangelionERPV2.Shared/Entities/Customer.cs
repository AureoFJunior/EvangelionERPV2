using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(Name))]
    [Index(nameof(Name))]
    public class Customer : BaseEntity
    {
        public Customer() { }

        public Customer(string name, string phoneNumber, string email, string adress)
        {
            Name = name;
            PhoneNumber = phoneNumber;
            Email = email;
            Adress = adress;
        }

        public string Name { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Adress { get; set; } = "";

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;
        public virtual Enterprise? Enterprise { get; set; } = null;

        public virtual IEnumerable<Order>? Order { get; set; } = null;
    }
}