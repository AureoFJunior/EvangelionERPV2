namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// How far a terminal sale has got in reaching the server.
    /// </summary>
    /// <remarks>
    /// Values are explicit because they are persisted in <c>PdvSale.Status</c>: reordering the
    /// members must never silently reinterpret rows already written.
    ///
    /// A sale is only ever created server-side by a sync call, so <see cref="Pending"/> exists for
    /// the window inside <c>SyncSaleAsync</c> where the row is committed but the order is not yet
    /// linked. It is not a queue the server drains — the queue lives on the terminal.
    /// </remarks>
    public enum EnumPdvSaleStatus
    {
        /// <summary>Recorded, order not yet linked. A row stuck here means sync died mid-transaction.</summary>
        Pending = 1,

        /// <summary>Linked to an <c>Order</c>. The terminal may drop its local copy.</summary>
        Synced = 2,

        /// <summary>Refused permanently (bad payload, closed period). The terminal stops retrying.</summary>
        Rejected = 3
    }
}
