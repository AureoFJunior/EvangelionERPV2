namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// Why a <c>StockMovement</c> happened.
    /// </summary>
    /// <remarks>
    /// Values are explicit because they are persisted in <c>StockMovement.Type</c>: reordering the
    /// members must never silently reinterpret rows already written.
    /// </remarks>
    public enum EnumStockMovementType
    {
        /// <summary>Units left because an order consumed them.</summary>
        Sale = 1,

        /// <summary>Units came back because an order was refunded.</summary>
        Refund = 2,

        /// <summary>Units arrived from a supplier.</summary>
        Receipt = 3,

        /// <summary>A human corrected the count.</summary>
        Adjustment = 4
    }
}
