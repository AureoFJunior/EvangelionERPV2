using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

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
        public string Currency { get; set; } = "BRL";
        public decimal CurrentBalance { get; set; } = 0;
        public bool ShouldSendMonthlyBilling { get; set; } = false;

        /// <summary>
        /// Whether the dunning sweeper may mail this tenant's late payers.
        /// </summary>
        public bool ShouldSendDunning { get; set; } = false;

        /// <summary>
        /// The last day the books are closed through. Null means nothing is closed.
        /// </summary>
        public DateTime? ClosedThroughUtc { get; set; } = null;

        /// <summary>
        /// The shop's Stripe connected account, or null if it has none.
        /// </summary>
        [MaxLength(64)]
        public string? StripeConnectedAccountId { get; set; }

        /// <summary>
        /// Whether that account may actually take PIX.
        /// </summary>
        public bool PixPaymentsEnabled { get; set; }

        [JsonIgnore]
        public virtual IEnumerable<Customer>? Customer { get; set; } = null;
        [JsonIgnore]
        public virtual IEnumerable<Order>? Order { get; set; } = null;
        [JsonIgnore]
        public virtual IEnumerable<User>? User { get; set; } = null;
        [JsonIgnore]
        public virtual IEnumerable<Product>? Product { get; set; } = null;
    }
}
