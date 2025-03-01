using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(Name))]
    [Index(nameof(Name))]
    public class Enterprise : BaseEntity
    {
        public Enterprise() { }

        public Enterprise(string name, string phoneNumber, string email, string adress, bool shouldSendMonthlyBilling)
        {
            Name = name;
            PhoneNumber = phoneNumber;
            Email = email;
            Adress = adress;
            ShouldSendMonthlyBilling = shouldSendMonthlyBilling;
        }

        public string Name { get; set; } = "";
        public string PhoneNumber { get; set; } = "";
        public string Email { get; set; } = "";
        public string Adress { get; set; } = "";
        public bool ShouldSendMonthlyBilling { get; set; } = false;
        public virtual IEnumerable<Customer>? Customer { get; set; } = null;
        public virtual IEnumerable<Order>? Order { get; set; } = null;
    }
}