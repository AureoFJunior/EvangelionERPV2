namespace EvangelionERPV2.NFeModule.Application.Configs
{
    public class NFeSettings
    {
        public bool Enabled { get; set; } = false;
        public string Environment { get; set; } = "Homologation";
        public string StateCode { get; set; } = "RS";
        public string Series { get; set; } = "1";
        public int StartingNumber { get; set; } = 1;
    }
}
