/** A vacation request that waits for the decision of the signed-in person (`GET /api/approvals/pending`). Dates are ISO dates. */
export interface PendingApproval {
  absenceId: string;
  employeeId: string;
  employeeName: string;
  from: string;
  to: string;
}
