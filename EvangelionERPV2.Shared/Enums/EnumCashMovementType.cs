namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// Why cash moved in or out of an open <c>CashSession</c>.
    /// </summary>
    public enum EnumCashMovementType
    {
        /// <summary>The opening float the operator started the session with.</summary>
        Opening = 1,

        /// <summary>Cash pulled out mid-shift, usually to a safe. Negative against the drawer.</summary>
        Sangria = 2,

        /// <summary>Cash added mid-shift, usually to make change. Positive against the drawer.</summary>
        Suprimento = 3,

        /// <summary>The cash portion of a sale. Card, PIX and voucher tenders never write this.</summary>
        Sale = 4,

        /// <summary>The counted amount removed when the session closed.</summary>
        Closing = 5,

        /// <summary>Cash returned to a customer for a cancelled sale.</summary>
        SaleCancellation = 6
    }
}
