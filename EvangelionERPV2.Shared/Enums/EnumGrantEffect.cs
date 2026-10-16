namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>What a per-user grant does: add a permission, or take one away that the role would give.</summary>
    public enum EnumGrantEffect : byte
    {
        Allow = 0,
        Deny = 1
    }
}
