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
        /// <remarks>
        /// Per-tenant opt-in for the same reason <see cref="ShouldSendMonthlyBilling"/> is one: the
        /// cadence itself is installation-wide configuration, but permission to write to somebody
        /// else's customers is the tenant's to give. Off by default — a tenant that upgrades into
        /// this feature must not discover it by having its customers emailed.
        /// </remarks>
        public bool ShouldSendDunning { get; set; } = false;

        /// <summary>
        /// The last day the books are closed through. Null means nothing is closed.
        /// </summary>
        /// <remarks>
        /// One column rather than a table of periods: months close forward and stay closed, and
        /// nobody reopens February while March is shut. Reopening is the same column moved back,
        /// which is why <see cref="Enterprise"/> is audited — the trail is the only record that a
        /// closed month was ever unlocked.
        ///
        /// Settling a boleto that fell due in a closed month is deliberately still allowed: the
        /// lock is about writing documents into a closed period, not about refusing money.
        /// </remarks>
        public DateTime? ClosedThroughUtc { get; set; } = null;

        /// <summary>
        /// The shop's Stripe connected account, or null if it has none.
        /// </summary>
        /// <remarks>
        /// One per Enterprise, which is why this is a column and not a table. Charges are created
        /// with <c>on_behalf_of</c> this account so the shop is the merchant of record — not a
        /// preference: NFC-e requires the CNPJ on the fiscal document and the CNPJ receiving the
        /// money to be the same, and a destination charge with the platform as MoR breaks that.
        ///
        /// Not a secret. The platform's Stripe key is never stored here or anywhere in this
        /// database; it is resolved per request from AWS Secrets Manager.
        /// </remarks>
        [MaxLength(64)]
        public string? StripeConnectedAccountId { get; set; }

        /// <summary>
        /// Whether that account may actually take PIX.
        /// </summary>
        /// <remarks>
        /// Stripe PIX on a BR account is invite-only, so having a connected account is not the same
        /// as being able to charge with it. Off by default: a shop discovers this feature by being
        /// enabled for it, never by a till offering a payment method that then fails at the counter.
        /// </remarks>
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
