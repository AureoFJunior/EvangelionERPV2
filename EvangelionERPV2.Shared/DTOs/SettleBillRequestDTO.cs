namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Manual settlement of a boleto — the "the customer paid, mark it" path that exists until CNAB
    /// retorno does it from the bank (M9). Deliberately carries the received amount and the moment
    /// rather than inferring them, because a payer settles late, with interest, or short.
    /// </summary>
    public sealed class SettleBillRequestDTO
    {
        public decimal PaidAmount { get; set; }

        /// <summary>When the money landed. Null means now.</summary>
        public DateTime? PaidAt { get; set; }

        public string? SettlementProtocol { get; set; }
    }
}
