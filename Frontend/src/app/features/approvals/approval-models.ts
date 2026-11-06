/** A vacation request that waits for the decision of the signed-in person (`GET /api/approvals/pending`). Dates are ISO dates. */
export interface PendingApproval {
  absenceId: string;
  employeeId: string;
  employeeName: string;
  from: string;
  to: string;
  /** The main store of the person (where colleagues' absences are looked up); `null` or missing if not known. */
  storeId?: string | null;
}
