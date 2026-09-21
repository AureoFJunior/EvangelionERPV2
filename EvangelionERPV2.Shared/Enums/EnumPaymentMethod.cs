namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// How a PDV sale was tendered. One sale can carry several of these.
    /// </summary>
    public enum EnumPaymentMethod
    {
        /// <summary>Notes and coins. The only method that can produce change.</summary>
        Cash = 1,

        /// <summary>Debit card, settled same day.</summary>
        Debit = 2,

        /// <summary>Credit card, possibly in installments.</summary>
        Credit = 3,

        /// <summary>PIX transfer, instant.</summary>
        Pix = 4,

        /// <summary>Meal/food voucher card (VR, Sodexo and friends).</summary>
        Voucher = 5,

        /// <summary>Paid against the customer's account with the store — becomes a receivable.</summary>
        StoreCredit = 6,

        /// <summary>Boleto issued for the sale — becomes a receivable, see the Bills module.</summary>
        Boleto = 7,

        /// <summary>Anything the store settles by hand. Forces an operator note.</summary>
        Other = 99,

        /// <summary>
        /// A sale settled by two or more different methods. Order-level only.
        /// </summary>
        Mixed = 98
    }
}
