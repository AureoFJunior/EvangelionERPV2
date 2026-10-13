namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// Lifecycle of a till session (caixa).
    /// </summary>
    public enum EnumCashSessionStatus
    {
        /// <summary>Accepting sales. At most one per terminal per enterprise, enforced by index.</summary>
        Open = 1,

        /// <summary>Counted and closed. Immutable — no sale may attach to it afterwards.</summary>
        Closed = 2
    }
}
