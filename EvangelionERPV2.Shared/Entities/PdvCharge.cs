using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One attempt to take money through a payment provider, and what became of it.
    /// </summary>
    /// <remarks>
    /// The till polls this row and never Stripe: a terminal that could reach Stripe directly is a
    /// terminal holding a key, and under Connect a leaked key moves a connected account's money.
    ///
    /// <see cref="FeeAmount"/> exists for one reason. 1,19% on PIX is expensive for retail, and a
    /// bank PIX API charges cents per cob rather than a percentage. Choosing between them now, with
    /// no data, is guessing — so every settled charge records what it cost, and in a quarter that
    /// is the number that either negotiates the rate down or justifies a second driver.
    /// </remarks>
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
        /// <remarks>
        /// This plus "a terminal status never changes again" is the whole of the webhook's
        /// idempotency. A replayed event id matches and is a no-op; a late event arriving after the
        /// charge settled is a no-op too. Cheaper and harder to get wrong than a table of every
        /// event we were ever sent — if an audit of those is ever wanted, that table is the
        /// upgrade path and this column is unaffected.
        /// </remarks>
        [MaxLength(128)]
        public string? ProviderEventId { get; set; }
    }
}
