namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// How a PDV sale was tendered. One sale can carry several of these.
    /// </summary>
    /// <remarks>
    /// Values are explicit because they are persisted in <c>PdvSalePayment.Method</c>: reordering
    /// the members must never silently reinterpret rows already written.
    ///
    /// The set is deliberately the NFC-e <c>tPag</c> vocabulary collapsed to what a till actually
    /// offers, so the fiscal mapping is a lookup and not a judgement call:
    /// Cash-01, Credit-03, Debit-04, Voucher-05, StoreCredit-05, Boleto-15, Pix-17, Other-99.
    /// </remarks>
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
        Other = 99
    }
}
