namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// How far a terminal sale has got in reaching the server.
    /// </summary>
    public enum EnumPdvSaleStatus
    {
        /// <summary>Recorded, order not yet linked. A row stuck here means sync died mid-transaction.</summary>
        Pending = 1,

        /// <summary>Linked to an <c>Order</c>. The terminal may drop its local copy.</summary>
        Synced = 2,

        /// <summary>Refused permanently (bad payload, closed period). The terminal stops retrying.</summary>
        Rejected = 3,

        /// <summary>A server request owns this sale while it creates or resumes the linked order.</summary>
        Processing = 4
    }
}
