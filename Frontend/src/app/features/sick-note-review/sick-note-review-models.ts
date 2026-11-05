/** One sick note in the HR overview (`GET /api/sick-notes`). Dates are ISO dates, the open end of a sick note is `null`. */
export interface SickNoteOverview {
  absenceId: string;
  employeeId: string;
  employeeName: string;
  storeId: string | null;
  from: string;
  to: string | null;
  certificateStatus: string;
  certifiedThrough: string | null;
  remindersSent: number;
  lastReminderAt: string | null;
}

export interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

/** What is known of an uploaded certificate (`GET /api/absences/{id}/certificates`): never the content, never a file name. */
export interface CertificateDocument {
  id: string;
  absenceId: string;
  state: string;
  certifiedFrom: string;
  certifiedTo: string;
  uploadedAt: string;
  uploadedBy: string | null;
  contentType: string;
  length: number;
  rejectionReason: string | null;
  hasContent: boolean;
  canOpen: boolean;
}

/** The filter above the list; `null` is "all". The order is the order of the buttons. */
export const STATUS_FILTERS: readonly { value: string | null; label: string }[] = [
  { value: 'Submitted', label: 'Wartet auf Prüfung' },
  { value: 'Missing', label: 'AU fehlt' },
  { value: 'Rejected', label: 'Abgelehnt' },
  { value: 'Verified', label: 'Geprüft' },
  { value: null, label: 'Alle' },
];

const CERTIFICATES: Record<string, string> = {
  Missing: 'AU fehlt',
  Submitted: 'Wartet auf Prüfung',
  Verified: 'AU geprüft',
  Rejected: 'AU abgelehnt',
};

export const certificateStatusLabel = (status: string): string => CERTIFICATES[status] ?? status;

/** The fixed reasons HR can give for turning a certificate down (no free text: it must not carry health data). */
export const REJECTION_REASONS: readonly { value: string; label: string }[] = [
  { value: 'Unreadable', label: 'Nicht lesbar' },
  { value: 'WrongPerson', label: 'Falsche Person' },
  { value: 'WrongPeriod', label: 'Falscher Zeitraum' },
  { value: 'NotACertificate', label: 'Keine AU' },
];

export const rejectionLabel = (reason: string | null): string => REJECTION_REASONS.find((r) => r.value === reason)?.label ?? reason ?? '';

/** Whether HR still has to decide on this document. */
export const isOpenForReview = (document: CertificateDocument): boolean => document.state === 'Submitted';

/** `1536` becomes `1,5 KB`. */
export function formatSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const kb = bytes / 1024;
  return kb < 1024 ? `${kb.toFixed(1).replace('.', ',')} KB` : `${(kb / 1024).toFixed(1).replace('.', ',')} MB`;
}
