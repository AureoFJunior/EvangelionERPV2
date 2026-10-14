using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using EvangelionERPV2.Shared.Enums;
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

        #region Storefront (Limen)

        /// <summary>Invisible to the storefront until this is set; see <c>SPEC-ecommerce-storefront.md</c> §4.2.</summary>
        public bool IsListedOnStorefront { get; set; }
        public EnumIamMode IamMode { get; set; } = EnumIamMode.Role;
        public DateTime? IamSwitchedAt { get; set; }

        /// <summary>First time the shop was listed. Shown as "na Limen desde".</summary>
        public DateTime? StorefrontListedAt { get; set; }

        /// <summary><c>/loja/{slug}</c>.</summary>
        [MaxLength(60)]
        public string? StorefrontSlug { get; set; }

        [MaxLength(500)]
        public string? StorefrontDescription { get; set; }

        [MaxLength(500)]
        public string? StorefrontLogoUrl { get; set; }

        /// <summary>Encrypted S3 key of the uploaded logo; served to Limen, never serialised.</summary>
        [MaxLength(512)]
        [JsonIgnore]
        public string? StorefrontLogoAdress { get; set; }

        /// <summary>How fast the shop usually answers shoppers, in hours (1–168); null hides "responde em".</summary>
        public int? StorefrontReplyTimeHours { get; set; }

        /// <summary>Returns, exchanges and warranty, plain text. Null shows Limen's platform defaults.</summary>
        [MaxLength(4000)]
        public string? StorefrontPolicies { get; set; }

        /// <summary>Encrypted S3 key of the store's 16:5 cover banner.</summary>
        [MaxLength(512)]
        [JsonIgnore]
        public string? StorefrontCoverAdress { get; set; }

        /// <summary>Flat fee per shop per order.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal StorefrontShippingFee { get; set; }

        /// <summary>Business days to dispatch.</summary>
        public int StorefrontHandlingDays { get; set; }

        /// <summary>Business days in transit, fastest and slowest.</summary>
        public int StorefrontDeliveryDaysMin { get; set; }

        public int StorefrontDeliveryDaysMax { get; set; }

        #endregion

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
