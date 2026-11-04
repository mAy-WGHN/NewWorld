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
}

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
  a.type === 'Sickness' && a.status === 'Reported' && (a.certificateStatus === 'Missing' || a.certificateStatus === 'Rejected');

/** The own sick notes: those that still need an AU first (apart, each note only once), the others below; newest first in both. */
export function splitSickNotes(absences: readonly Absence[]): { attention: Absence[]; others: Absence[] } {
  const sickNotes = absences.filter((a) => a.type === 'Sickness').sort((a, b) => b.from.localeCompare(a.from));
  return { attention: sickNotes.filter(needsCertificate), others: sickNotes.filter((a) => !needsCertificate(a)) };
}

const ERRORS: Record<string, string> = {
  InvalidPeriod: 'Der Zeitraum ist nicht gültig.',
  AfterExit: 'Der Zeitraum liegt nach deinem Austritt.',
  Overlap: 'Der Zeitraum überschneidet sich mit einer anderen Abwesenheit.',
  NoEntitlement: 'Für dieses Jahr ist kein Urlaubsanspruch hinterlegt.',
  InsufficientBalance: 'Dafür reichen deine Urlaubstage nicht aus.',
  NoApprover: 'Es ist niemand für die Genehmigung hinterlegt. Bitte wende dich an die Personalabteilung.',
  InvalidState: 'Das ist im aktuellen Stand nicht möglich.',
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
