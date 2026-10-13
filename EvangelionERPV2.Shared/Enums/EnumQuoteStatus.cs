namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// Where a quote is in its short life.
    /// </summary>
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
        Converted = 6,

        /// <summary>An order conversion currently owns this quote.</summary>
        Converting = 7
    }
}
