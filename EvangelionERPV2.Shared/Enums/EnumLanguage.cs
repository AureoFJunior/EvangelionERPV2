namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// The user's interface language, stored on <see cref="Entities.User.Language"/> as its integer.
    /// </summary>
    /// <remarks>
    /// Append only. The value is persisted, so reordering these renames every existing user's
    /// language, and the frontend maps the same integers in utils/language.ts.
    /// </remarks>
    public enum EnumLanguage
    {
        English = 0,
        Portuguese = 1,
        Spanish = 2,
        Japanese = 3,
        Chinese = 4
    }
}
