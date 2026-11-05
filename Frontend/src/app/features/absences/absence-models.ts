import { HttpErrorResponse } from '@angular/common/http';

/** An absence of the signed-in person (`GET /api/me/absences`). Dates are ISO dates (`yyyy-MM-dd`), the open end of a sick note is `null`. */
export interface Absence {
  id: string;
  employeeId: string;
  type: string | null;
  status: string;
  from: string;
  to: string | null;
  certificateStatus: string | null;
  certifiedThrough: string | null;
  /** Sickness only: `First` or `FollowUp` (`Unknown` for sick notes from before the distinction). */
  sickNoteKind?: string | null;
  /** Sickness only: the AU is an eAU that HR finds in DATEV. */
  electronicCertificate?: boolean | null;
}

/** What the form "Krankmeldung" hands over: the sick note and, optionally, the AU that goes with it. */
export interface SickNoteInput {
  kind: SickNoteKind | null;
  from: string;
  to: string;
  /** The AU is an eAU (then nothing is uploaded). */
  electronic: boolean;
  /** The AU as a file, if the person has it already; it covers the period of the sick note. */
  file: File | null;
}

export type SickNoteKind = 'First' | 'FollowUp';

/** The vacation account of a year (`GET /api/me/vacation-balance`). */
export interface VacationBalance {
  year: number;
  entitlement: number;
  taken: number;
  planned: number;
  remaining: number;
  contractualAnnualDays?: number;
  contractualDays?: number;
  extraDays?: number;
  forfeitedExtraDays?: number;
}

const TYPES: Record<string, string> = { Sickness: 'Krankmeldung', Vacation: 'Urlaub' };

const STATUSES: Record<string, string> = {
  Requested: 'Beantragt',
  Approved: 'Genehmigt',
  Rejected: 'Abgelehnt',
  Withdrawn: 'Zurückgezogen',
  Cancelled: 'Storniert',
  Reported: 'Gemeldet',
};

const CERTIFICATES: Record<string, string> = {
  Missing: 'AU fehlt',
  Submitted: 'AU eingereicht',
  Verified: 'AU geprüft',
  Rejected: 'AU abgelehnt',
};

const KINDS: Record<string, string> = { First: 'Erstkrankmeldung', FollowUp: 'Folgekrankmeldung' };

/** Empty for sick notes from before the distinction existed. */
export const kindLabel = (kind: string | null | undefined): string => (kind && KINDS[kind]) || '';

export const typeLabel = (type: string | null): string => (type && TYPES[type]) || 'Abwesenheit';

/** An unknown state is shown as it is, so a new state of the service never disappears silently. */
export const statusLabel = (status: string): string => STATUSES[status] ?? status;

export const certificateLabel = (status: string | null): string => (status && CERTIFICATES[status]) || '';

/** `2026-11-04` becomes `04.11.2026` (no Date object: no time zone can shift the day). */
export function formatDate(iso: string | null): string {
  if (!iso) {
    return '';
  }
  const [year, month, day] = iso.split('-');
  return `${day}.${month}.${year}`;
}

export function formatPeriod(from: string, to: string | null): string {
  if (!to) {
    return `ab ${formatDate(from)} (offen)`;
  }
  return from === to ? formatDate(from) : `${formatDate(from)} – ${formatDate(to)}`;
}

/** What the file chooser offers (the type of the file is checked again after the choice and by the service from the content). */
export const CERTIFICATE_ACCEPT = '.pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg';

/** The service takes files up to 10 MB. */
export const MAX_CERTIFICATE_BYTES = 10 * 1024 * 1024;

const CERTIFICATE_TYPES = ['application/pdf', 'image/jpeg', 'image/png'];
const CERTIFICATE_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png'];

/** The message for a file that cannot be an AU (only PDF, JPEG and PNG, not empty, not too big), or `null` if it is fine. */
export function validateCertificateFile(file: File): string | null {
  const name = file.name.toLowerCase();
  const typeOk = CERTIFICATE_TYPES.includes(file.type) || (!file.type && CERTIFICATE_EXTENSIONS.some((e) => name.endsWith(e)));
  const extensionOk = CERTIFICATE_EXTENSIONS.some((e) => name.endsWith(e));
  if (!typeOk || !extensionOk) {
    return 'Erlaubt sind nur PDF, JPEG und PNG.';
  }
  if (file.size === 0 || file.size > MAX_CERTIFICATE_BYTES) {
    return 'Die Datei ist leer oder zu groß (höchstens 10 MB).';
  }
  return null;
}

/** The message for a sick note that cannot be sent as it is, or `null` if it is fine. */
export function validateSickNote(input: SickNoteInput): string | null {
  if (!input.kind) {
    return 'Bitte wählen: Erst- oder Folgekrankmeldung.';
  }
  if (input.electronic && input.file) {
    return 'Bitte entweder die AU hochladen oder „eAU“ wählen.';
  }
  if (input.file) {
    const fileProblem = validateCertificateFile(input.file);
    if (fileProblem) {
      return fileProblem;
    }
  }
  return validatePeriod(input.from, input.to, true);
}

/** The message for a wrong period, or `null` if it is fine. ISO dates compare correctly as text. */
export function validatePeriod(from: string, to: string, toRequired: boolean): string | null {
  if (!from) {
    return 'Bitte einen Beginn angeben.';
  }
  if (!to) {
    return toRequired ? 'Bitte ein Ende angeben.' : null;
  }
  return to < from ? 'Das Ende liegt vor dem Beginn.' : null;
}

// What a row offers. The service decides in the end (e.g. vacation that has started cannot be withdrawn); this only avoids pointless buttons.
export const canWithdraw = (a: Absence): boolean => a.type === 'Vacation' && (a.status === 'Requested' || a.status === 'Approved');
export const canEnd = (a: Absence): boolean => a.type === 'Sickness' && a.status === 'Reported' && a.to === null;
export const canUpload = (a: Absence): boolean => a.type === 'Sickness' && a.status === 'Reported';

/**
 * HR still needs an AU for an active sick note if none was handed in or HR turned the one handed in down.
 * An AU that was handed in and waits for the check, or that was checked, needs nothing more from the person.
 */
export const needsCertificate = (a: Absence): boolean =>
  a.type === 'Sickness' && a.status === 'Reported' && !a.electronicCertificate && (a.certificateStatus === 'Missing' || a.certificateStatus === 'Rejected');

/** Whether the person can still mark the AU as an eAU: not while an uploaded AU waits for the check or was confirmed. */
export const canMarkElectronic = (a: Absence): boolean =>
  a.type === 'Sickness' && a.status === 'Reported' && a.certificateStatus !== 'Submitted' && a.certificateStatus !== 'Verified';

/** The own sick notes: those that still need an AU first (apart, each note only once), the others below; newest first in both. */
export function splitSickNotes(absences: readonly Absence[]): { attention: Absence[]; others: Absence[] } {
  const sickNotes = absences.filter((a) => a.type === 'Sickness').sort((a, b) => b.from.localeCompare(a.from));
  return { attention: sickNotes.filter(needsCertificate), others: sickNotes.filter((a) => !needsCertificate(a)) };
}

/** The own vacation: requests that still wait for a decision first (apart, each only once), the others below; newest first in both. */
export function splitVacation(absences: readonly Absence[]): { pending: Absence[]; others: Absence[] } {
  const vacation = absences.filter((a) => a.type === 'Vacation').sort((a, b) => b.from.localeCompare(a.from));
  return { pending: vacation.filter((a) => a.status === 'Requested'), others: vacation.filter((a) => a.status !== 'Requested') };
}

const ERRORS: Record<string, string> = {
  InvalidPeriod: 'Der Zeitraum ist nicht gültig.',
  AfterExit: 'Der Zeitraum liegt nach deinem Austritt.',
  Overlap: 'Der Zeitraum überschneidet sich mit einer anderen Abwesenheit.',
  NoEntitlement: 'Für dieses Jahr ist kein Urlaubsanspruch hinterlegt.',
  InsufficientBalance: 'Dafür reichen deine Urlaubstage nicht aus.',
  NoApprover: 'Es ist niemand für die Genehmigung hinterlegt. Bitte wende dich an die Personalabteilung.',
  InvalidState: 'Das ist im aktuellen Stand nicht möglich.',
  InvalidKind: 'Bitte wählen: Erst- oder Folgekrankmeldung.',
  NoPredecessor: 'Eine Folgekrankmeldung muss direkt an eine vorherige Krankmeldung anschließen. Wähle sonst „Erstkrankmeldung“.',
  NotAllowed: 'Das darfst du nicht.',
  AlreadyStarted: 'Der Urlaub hat schon begonnen und kann nicht mehr zurückgezogen werden.',
  WaitingPeriod: 'Für neue Mitarbeitende gilt noch die Wartezeit für Urlaub.',
  FileTooLarge: 'Die Datei ist zu groß oder leer.',
  UnsupportedFileType: 'Erlaubt sind nur PDF, JPEG und PNG.',
  FileRejected: 'Die Datei wurde abgelehnt.',
  TooManyDocuments: 'Es sind schon zu viele Dokumente hochgeladen.',
  NotFound: 'Der Eintrag wurde nicht gefunden.',
  UnknownPerson: 'Der Eintrag wurde nicht gefunden.',
};

export const GENERIC_ERROR = 'Das hat nicht geklappt. Bitte erneut versuchen.';

export const errorMessage = (title: string | undefined): string => (title && ERRORS[title]) || GENERIC_ERROR;

/** The `title` of a problem answer of the services (the error code), if there is one. */
export function problemTitle(error: unknown): string | undefined {
  if (error instanceof HttpErrorResponse && typeof error.error === 'object' && error.error !== null) {
    const title = (error.error as { title?: unknown }).title;
    return typeof title === 'string' ? title : undefined;
  }
  return undefined;
}
