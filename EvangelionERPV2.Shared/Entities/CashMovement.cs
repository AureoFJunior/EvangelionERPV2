using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Append-only ledger of every cash movement inside a <see cref="CashSession"/>.
    /// </summary>
    /// <remarks>
    /// Same shape and same reasoning as <see cref="StockMovement"/>: a drawer total on its own says
    /// what is there and never why, so the expected amount at close is the sum of these rows and
    /// nothing else. If the two ever disagree, the ledger is right and the total is a bug.
    ///
    /// Only cash lands here. A card or PIX tender is money the store received but not money in the
    /// drawer, so counting it would guarantee a difference at every single close.
    /// </remarks>
    [Index(nameof(CashSessionId), nameof(CreatedAt))]
    [Index(nameof(EnterpriseId), nameof(ClientMovementId), IsUnique = true)]
    [Table("CashMovement")]
    public class CashMovement : BaseEntity
    {
        /// <summary>
        /// The terminal's own id for this movement, so a re-sent sangria does not empty the drawer
        /// twice. Same reasoning as <see cref="CashSession.ClientSessionId"/>.
        /// </summary>
        /// <remarks>
        /// A movement the server generated itself — the opening float, the cash leg of a sale —
        /// gets a fresh Guid here rather than null, because a nullable column cannot carry a unique
        /// index that means anything in SQL Server.
        /// </remarks>
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
