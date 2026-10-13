namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// Where one período aquisitivo stands. Explicit values: they are persisted in
    /// <c>EmployeeVacationAccrual.Status</c>, so reordering the members must never silently
    /// reinterpret rows already written.
    /// </summary>
    public enum EnumVacationAccrualStatus
    {
        /// <summary>The 12-month aquisitivo is still running; nothing is owed yet.</summary>
        Accruing = 1,

        /// <summary>The aquisitivo closed and the full entitlement is untouched.</summary>
        Available = 2,

        /// <summary>Some days scheduled, taken or sold; a balance remains.</summary>
        PartiallyTaken = 3,

        /// <summary>Every entitled day is accounted for. Terminal.</summary>
        Closed = 4,

        /// <summary>
        /// The concessivo window closed with days still owed (CLT art. 137): the remaining days are
        /// now payable <b>in dobro</b>. Set by a check, never by a person, because it is a
        /// consequence of the calendar rather than a decision.
        /// </summary>
        Lapsed = 5
    }
}
