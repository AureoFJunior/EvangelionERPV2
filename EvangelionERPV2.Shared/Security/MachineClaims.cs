namespace EvangelionERPV2.Shared.Security
{
    /// <summary>
    /// Claims that mark a token as belonging to a trusted machine caller (the background
    /// workers' self-API service login) rather than an interactive user session. Used to
    /// gate unscoped, all-tenant broadcast endpoints so they cannot be reached by ordinary
    /// tenant admins.
    /// </summary>
    public static class MachineClaims
    {
        public const string ClientType = "client_type";
        public const string SelfApiValue = "self_api";
    }
}
