namespace EvangelionERPV2.BillsModule.Application.Configs
{
    public class BillsSettings
    {
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Attaches the rendered bill PDF to the customer email. Requires a working headless
        /// browser; turn it off to fall back to a body carrying just the digitable line.
        /// </summary>
        public bool AttachPdfToBillEmail { get; set; } = true;

        public int BankCode { get; set; } = 237;
        public string BeneficiaryDocument { get; set; } = "";
        public string BeneficiaryName { get; set; } = "";
        public string BeneficiaryAddress { get; set; } = "";
        public string BeneficiaryAddressNumber { get; set; } = "";
        public string BeneficiaryAddressComplement { get; set; } = "";
        public string BeneficiaryNeighborhood { get; set; } = "";
        public string BeneficiaryCity { get; set; } = "";
        public string BeneficiaryState { get; set; } = "";
        public string BeneficiaryZipCode { get; set; } = "";
        public string BeneficiaryNotes { get; set; } = "";
        public string BeneficiaryCode { get; set; } = "";
        public string BeneficiaryCodeDigit { get; set; } = "";
        public string TransmissionCode { get; set; } = "";
        public string Agency { get; set; } = "";
        public string AgencyDigit { get; set; } = "";
        public string Account { get; set; } = "";
        public string AccountDigit { get; set; } = "";
        public string Wallet { get; set; } = "";
        public string WalletVariation { get; set; } = "";
        public int WalletType { get; set; } = 1;
        public int RegistrationType { get; set; } = 1;
        public int PrintType { get; set; } = 2;
        public int DocumentType { get; set; } = 1;
        public int NossoNumeroLength { get; set; } = 8;

        /// <summary>
        /// First NossoNumero of the range the convênio grants. Seeds the counter row the first time
        /// a boleto is generated for an enterprise; changing it afterwards does nothing, because a
        /// counter that restarted would re-issue numbers already handed to payers.
        /// </summary>
        public long NossoNumeroStart { get; set; } = 1;

        /// <summary>
        /// Last NossoNumero the convênio allows. 0 means "the widest value that still fits
        /// <see cref="NossoNumeroLength"/> digits" — a safe default only because the number is
        /// zero-padded to that length and a wider one would not fit the field at all.
        /// </summary>
        public long NossoNumeroCeiling { get; set; } = 0;
        public string Instructions { get; set; } = "";
        public string DefaultPayerDocument { get; set; } = "00000000000";
        public string DefaultPayerName { get; set; } = "Customer";
        public string DefaultPayerAddress { get; set; } = "";
    }
}

