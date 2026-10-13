namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>The number a Limen shopper and the store both see for one order: "LM-" + the id's first 8 hex digits.</summary>
    public static class StorefrontOrderNumber
    {
        public static string For(Guid orderId) => "LM-" + orderId.ToString("N")[..8].ToUpperInvariant();
    }
}
