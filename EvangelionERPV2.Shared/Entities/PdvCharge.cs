using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One attempt to take money through a payment provider, and what became of it.
    /// </summary>
    [Index(nameof(EnterpriseId), nameof(ProviderIntentId), IsUnique = true)]
    [Index(nameof(EnterpriseId), nameof(ClientSaleId))]
    [Table("PdvCharge")]
    public class PdvCharge : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        /// <summary>The basket this was raised against, as the terminal minted it.</summary>
        public Guid ClientSaleId { get; set; }

        /// <summary>"stripe" today. A bank driver is a different value behind the same three calls.</summary>
        [MaxLength(32)]
        public string Provider { get; set; } = "stripe";

        [MaxLength(128)]
        public string ProviderIntentId { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        /// <summary><see cref="Enums.EnumPdvChargeStatus"/>.</summary>
        public int Status { get; set; }

        /// <summary>The copia-e-cola string. Rendered as a QR at the till; not a secret.</summary>
        [MaxLength(1024)]
        public string? QrPayload { get; set; }

        /// <summary>The PIX end-to-end id, once the bank has one. The proof a tender points at.</summary>
        [MaxLength(64)]
        public string? EndToEndId { get; set; }

        public DateTime ExpiresAt { get; set; }
        public DateTime? PaidAt { get; set; }

        /// <summary>What the provider took. Null until settlement says.</summary>
        public decimal? FeeAmount { get; set; }

        /// <summary>
        /// The provider event that last moved this row to a terminal status.
        /// </summary>
        [MaxLength(128)]
        public string? ProviderEventId { get; set; }
    }
}
