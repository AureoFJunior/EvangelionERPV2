namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// Where a quote is in its short life.
    /// </summary>
    /// <remarks>
    /// Values are explicit because they are persisted in <c>Quote.Status</c>: reordering the
    /// members must never silently reinterpret rows already written.
    ///
    /// <c>Expired</c> is stored but also derived — a quote past its ValidUntil is treated as
    /// expired whatever the column says, the same way <c>Bill.Overdue</c> is read off the clock.
    /// A sweeper that writes the column is not worth a daily job for a document nobody chases.
    /// </remarks>
    public enum EnumQuoteStatus
    {
        /// <summary>Being written. The only state in which items can still change.</summary>
        Draft = 1,

        /// <summary>Handed to the customer. Still convertible.</summary>
        Sent = 2,

        /// <summary>The customer said yes. Convertible, and the usual state at conversion.</summary>
        Accepted = 3,

        /// <summary>The customer said no. Terminal.</summary>
        Rejected = 4,

        /// <summary>Past its ValidUntil without an answer. Terminal.</summary>
        Expired = 5,

        /// <summary>Became an order. Terminal, and <c>ConvertedOrderId</c> names which one.</summary>
        Converted = 6
    }
}
