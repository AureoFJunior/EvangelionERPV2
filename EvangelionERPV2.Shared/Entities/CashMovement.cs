using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Append-only ledger of every cash movement inside a <see cref="CashSession"/>.
    /// </summary>
    [Index(nameof(CashSessionId), nameof(CreatedAt))]
    [Index(nameof(EnterpriseId), nameof(ClientMovementId), IsUnique = true)]
    [Table("CashMovement")]
    public class CashMovement : BaseEntity
    {
        /// <summary>
        /// The terminal's own id for this movement, so a re-sent sangria does not empty the drawer
        /// twice. Same reasoning as <see cref="CashSession.ClientSessionId"/>.
        /// </summary>
        public Guid ClientMovementId { get; set; }

        [ForeignKey(nameof(CashSession))]
        public Guid CashSessionId { get; set; }

        [JsonIgnore]
        public virtual CashSession? CashSession { get; set; }

        public Guid EnterpriseId { get; set; }

        /// <summary><see cref="Enums.EnumCashMovementType"/>.</summary>
        public int Type { get; set; }

        /// <summary>
        /// Signed: positive put cash in the drawer, negative took it out. Opening, Suprimento and
        /// Sale are positive; Sangria, Closing and SaleCancellation are negative.
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>Required for a sangria or suprimento, which are otherwise indistinguishable from theft.</summary>
        [MaxLength(200)]
        public string? Reason { get; set; }

        /// <summary>Who did it — not necessarily whoever opened the session.</summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// The <c>PdvSale</c> behind a Sale or SaleCancellation row, null for manual movements.
        /// Intentionally not a foreign key, matching <see cref="StockMovement.ReferenceId"/>.
        /// </summary>
        public Guid? ReferenceId { get; set; }
    }
}
