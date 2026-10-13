namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// What kind of HR record an <c>EmployeeAbsence</c> row is.
    /// </summary>
    public enum EnumEmployeeAbsenceType
    {
        /// <summary>Falta — the employee did not show up. Unjustified by default, so it cuts pay.</summary>
        Falta = 1,

        /// <summary>
        /// Atestado — a medical certificate covering the days. Justified by default: under
        /// Lei 605/49 art. 6 §1 / CLT art. 131, a valid atestado is a falta justificada and the
        /// employer still owes the day.
        /// </summary>
        Atestado = 2,

        /// <summary>
        /// Advertência — a written warning. A disciplinary record, not unworked time: it never
        /// reduces pay on its own, and the falta that triggered it (if any) is its own row.
        /// </summary>
        Advertencia = 3,

        /// <summary>
        /// Férias — scheduled statutory leave, written by <c>EmployeeVacationService</c> alongside
        /// the decrement on the entitlement ledger.
        /// </summary>
        Vacation = 4
    }
}
