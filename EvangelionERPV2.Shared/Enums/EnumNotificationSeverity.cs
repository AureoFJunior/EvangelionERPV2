namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// How loudly the bell should render an item. Stored as an int, so the numbers are the
    /// contract: appending a level is safe, renumbering an existing one is not.
    /// </summary>
    public enum EnumNotificationSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }
}
