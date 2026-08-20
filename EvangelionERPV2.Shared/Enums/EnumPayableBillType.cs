namespace EvangelionERPV2.Shared.Enums
{
    public enum EnumPayableBillType
    {
        Accounts,
        Loan,
        Credit,
        ProductStock,
        Taxes,
        OperatingCost,
        LogisticsCost,
        Other,
        Payroll,

        // Appended, never inserted: these values are persisted in PayableBill.BillType and go out
        // on the wire. Férias pay is its own kind because it is not the competência's payroll —
        // it is paid up front, against an earned entitlement, and draws down the provision that
        // payroll accrued for it.
        Vacation
    }
}
