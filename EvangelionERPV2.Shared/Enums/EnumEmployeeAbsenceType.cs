namespace EvangelionERPV2.Shared.Enums
{
    /// <summary>
    /// What kind of HR record an <c>EmployeeAbsence</c> row is.
    /// </summary>
    /// <remarks>
    /// One table, three kinds, because payroll has to answer "how many days did this employee
    /// actually work in this competência" with a single query — three tables would make that a
    /// three-way union on the hot path of every payroll run.
    ///
    /// Values are explicit because they are persisted in <c>EmployeeAbsence.Type</c>: reordering
    /// the members must never silently reinterpret rows already written.
    /// </remarks>
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
        /// <remarks>
        /// Never a falta. It is recorded here because payroll reads exactly one table to answer
        /// "was this person at work", and because CLT art. 130 counts <em>faltas injustificadas</em>
        /// against the next entitlement — férias must never reduce the leave they are taken from,
        /// which is why the row is always justified.
        /// </remarks>
        Vacation = 4
    }
}
