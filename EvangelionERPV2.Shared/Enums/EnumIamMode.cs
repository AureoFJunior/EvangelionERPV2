namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>Where an enterprise's access comes from: the user's role, or per-user grants (zero trust).</summary>
    public enum EnumIamMode : byte
    {
        Role = 0,
        Grants = 1
    }
}
