using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Append-only ledger of every change to <see cref="Product.StorageQuantity"/>.
    /// </summary>
    // One composite index rather than two: EnterpriseId leads so the replenishment window scan
    // seeks on it, and a per-product history lookup seeks on (EnterpriseId, ProductId).
    // ponytail: single index, add a dedicated (EnterpriseId, CreatedAt) one if the window scan
    // ever shows up as a hotspot.
    [Index(nameof(EnterpriseId), nameof(ProductId), nameof(CreatedAt))]
    [Table("StockMovement")]
    public class StockMovement : BaseEntity
    {
        [ForeignKey(nameof(Product))]
        public Guid ProductId { get; set; }

        [JsonIgnore]
        public virtual Product? Product { get; set; }

        public Guid EnterpriseId { get; set; }

        /// <summary>
        /// Signed: negative took units out, positive put them back. Same decimal(18,4) as
        /// StorageQuantity, because a Kg/Litre product moves in fractions.
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; }

        /// <summary>StorageQuantity as it stood after this movement was applied.</summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal BalanceAfter { get; set; }

        /// <summary><see cref="Enums.EnumStockMovementType"/>.</summary>
        public int Type { get; set; }

        /// <summary>
        /// What caused it — the order id for a sale or refund, the payable bill id for a receipt,
        /// null for a manual adjustment that has no document behind it. Intentionally not a
        /// foreign key: the target table differs per <see cref="Type"/>.
        /// </summary>
        public Guid? ReferenceId { get; set; }
    }
}
