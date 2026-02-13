namespace EvangelionERPV2.BillsModule.Application.Configs
{
    public class BillsSettings
    {
        public bool Enabled { get; set; } = false;
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
        public string Instructions { get; set; } = "";
        public string DefaultPayerDocument { get; set; } = "00000000000";
        public string DefaultPayerName { get; set; } = "Customer";
        public string DefaultPayerAddress { get; set; } = "";
    }
}

