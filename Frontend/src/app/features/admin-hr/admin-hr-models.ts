import { HttpErrorResponse } from '@angular/common/http';
import { errorMessage, problemTitle } from '../absences/absence-models';

// ---------------------------------------------------------------- shared helpers

/** What a text field holds: nothing (`null`), a number, or something that is not a number (`undefined`). A comma counts as decimal point. */
export function toNumber(text: string): number | null | undefined {
  const t = text.trim().replace(',', '.');
  if (t === '') {
    return null;
  }
  return /^-?\d+(\.\d+)?$/.test(t) ? Number(t) : undefined;
}

/** At most two decimals (the services reject more). */
export const hasAtMostTwoDecimals = (value: number): boolean => Math.abs(Math.round(value * 100) - value * 100) < 1e-9;

const ERRORS: Record<string, string> = {
  InvalidVacationRules: 'Die Urlaubsregeln sind nicht gültig. Bitte prüfe die Angaben.',
  InvalidRules: 'Die Regeln sind nicht gültig. Bitte prüfe die Angaben.',
  InvalidPeriod: 'Die Angaben sind nicht gültig. Bitte prüfe die Werte.',
  Invalid: 'Die Angaben sind nicht gültig. Bitte prüfe die Werte.',
  DuplicateCode: 'Dieser Code ist schon vergeben.',
  DuplicateNumber: 'Diese Personalnummer ist schon vergeben.',
  NumberSeriesNotSet: 'Der Nummernkreis ist noch nicht eingestellt.',
  NumberSeriesExhausted: 'Der Nummernkreis ist aufgebraucht.',
  UnknownRelationship: 'Dieses Arbeitsverhältnis gibt es nicht.',
  UnknownKind: 'Diese Art gibt es nicht.',
  SystemKind: 'Diese Art wird vom System gepflegt (Krank, Urlaub, Krank im Urlaub) und lässt sich nicht ändern.',
  NotAllowed: 'Das darfst du nicht.',
};

/** Plain-language text for a failed call of the services. */
export function describeError(error: unknown): string {
  const title = problemTitle(error);
  if (title && ERRORS[title]) {
    return ERRORS[title];
  }
  if (error instanceof HttpErrorResponse) {
    if (error.status === 403) {
      return 'Das darfst du nicht. Eine der Filialen liegt vielleicht außerhalb deiner Reichweite.';
    }
    if (error.status === 0) {
      return 'Der Dienst ist gerade nicht erreichbar. Bitte später erneut versuchen.';
    }
  }
  return errorMessage(title);
}

export const loadFailed = (what: string): string => `${what}: Das Laden hat nicht geklappt. Bitte später erneut versuchen.`;

/** A change recorded by the services (`null` while nothing was ever set: the default applies). */
export interface Audited {
  isDefault: boolean;
  changedAt: string | null;
  changedBy: string | null;
}

// ---------------------------------------------------------------- vacation (Urlaub)

export interface VacationRules extends Audited {
  extraDaysExpiryMonth: number | null;
  extraDaysExpiryDay: number | null;
  waitingPeriodMonths: number;
  /** `Accrued` or `Blocked`. */
  waitingPeriodMode: string;
  /** `Up`, `Nearest`, `Down` or `None`. */
  proRataRounding: string;
  proRataRoundingStep: number;
}

export interface SetVacationRulesRequest {
  extraDaysExpiryMonth: number | null;
  extraDaysExpiryDay: number | null;
  waitingPeriodMonths: number;
  waitingPeriodMode: string;
  proRataRounding: string;
  proRataRoundingStep: number;
}

export const WAITING_MODES = [
  { value: 'Accrued', label: 'Nur bis dahin erworbene Tage', hint: 'Neue Mitarbeitende dürfen schon Urlaub nehmen, aber nur so viele Tage, wie sie bis dahin erworben haben (1/12 je vollem Monat seit Eintritt).' },
  { value: 'Blocked', label: 'Kein Urlaub', hint: 'Neue Mitarbeitende können in der Wartezeit keinen Urlaub beantragen.' },
] as const;

export const ROUNDINGS = [
  { value: 'Up', label: 'Aufrunden', hint: 'Aufgerundet auf den nächsten Schritt (Standard: halbe Tage, 2,083 wird 2,5).' },
  { value: 'Nearest', label: 'Auf den nächsten Schritt runden', hint: 'Gerundet auf den nächstliegenden Schritt.' },
  { value: 'Down', label: 'Abrunden', hint: 'Abgerundet auf den vorherigen Schritt.' },
  { value: 'None', label: 'Nicht runden', hint: 'Der anteilige Anspruch bleibt unverändert, zum Beispiel 2,083 Tage.' },
] as const;

export const ROUNDING_STEPS = [
  { value: 0.5, label: 'halbe Tage' },
  { value: 1, label: 'ganze Tage' },
] as const;

export interface VacationForm {
  expires: boolean;
  month: string;
  day: string;
  waitingMonths: string;
  waitingMode: string;
  rounding: string;
  step: string;
}

export function vacationForm(rules: VacationRules): VacationForm {
  return {
    expires: rules.extraDaysExpiryMonth !== null && rules.extraDaysExpiryDay !== null,
    month: String(rules.extraDaysExpiryMonth ?? 3),
    day: String(rules.extraDaysExpiryDay ?? 31),
    waitingMonths: String(rules.waitingPeriodMonths),
    waitingMode: rules.waitingPeriodMode,
    rounding: rules.proRataRounding,
    step: String(rules.proRataRoundingStep),
  };
}

const daysInMonth = (month: number): number => new Date(2024, month, 0).getDate();

/** The request for the form, or the plain-language reason why it is not valid yet. */
export function vacationRequest(form: VacationForm): SetVacationRulesRequest | string {
  let month: number | null = null;
  let day: number | null = null;
  if (form.expires) {
    const m = toNumber(form.month);
    const d = toNumber(form.day);
    if (m == null || d == null || !Number.isInteger(m) || !Number.isInteger(d) || m < 1 || m > 12) {
      return 'Bitte Monat (1 bis 12) und Tag für den Verfall der Zusatztage angeben.';
    }
    if (d < 1 || d > daysInMonth(m)) {
      return 'Diesen Tag gibt es im gewählten Monat nicht.';
    }
    month = m;
    day = d;
  }
  const waiting = toNumber(form.waitingMonths);
  if (waiting == null || !Number.isInteger(waiting) || waiting < 0 || waiting > 24) {
    return 'Die Wartezeit muss eine ganze Zahl von 0 bis 24 Monaten sein.';
  }
  const step = toNumber(form.step);
  if (step !== 0.5 && step !== 1) {
    return 'Der Schritt der Rundung ist „halbe Tage“ oder „ganze Tage“.';
  }
  return {
    extraDaysExpiryMonth: month,
    extraDaysExpiryDay: day,
    waitingPeriodMonths: waiting,
    waitingPeriodMode: form.waitingMode,
    proRataRounding: form.rounding,
    proRataRoundingStep: step,
  };
}

export const MONTHS = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];

// ---------------------------------------------------------------- working time (Arbeitszeitkonto)

export interface WorkingTimeRules extends Audited {
  payoutThresholdHours: number;
  expiryMonths: number | null;
  bookMinusHours: boolean;
  maxMinusHours: number | null;
}

export interface SetWorkingTimeRulesRequest {
  payoutThresholdHours: number;
  expiryMonths: number | null;
  bookMinusHours: boolean;
  maxMinusHours: number | null;
}

export interface WorkingTimeForm {
  payout: string;
  expires: boolean;
  expiryMonths: string;
  bookMinus: boolean;
  limitMinus: boolean;
  maxMinus: string;
}

export function workingTimeForm(rules: WorkingTimeRules): WorkingTimeForm {
  return {
    payout: String(rules.payoutThresholdHours),
    expires: rules.expiryMonths !== null,
    expiryMonths: String(rules.expiryMonths ?? 12),
    bookMinus: rules.bookMinusHours,
    limitMinus: rules.maxMinusHours !== null,
    maxMinus: String(rules.maxMinusHours ?? 0),
  };
}

export function workingTimeRequest(form: WorkingTimeForm): SetWorkingTimeRulesRequest | string {
  const payout = toNumber(form.payout);
  if (payout == null || payout < 0 || payout > 1000 || !hasAtMostTwoDecimals(payout)) {
    return 'Die Auszahlungsschwelle muss zwischen 0 und 1000 Stunden liegen (höchstens zwei Nachkommastellen).';
  }
  let months: number | null = null;
  if (form.expires) {
    const m = toNumber(form.expiryMonths);
    if (m == null || !Number.isInteger(m) || m < 1 || m > 120) {
      return 'Die Verfallsfrist muss eine ganze Zahl von 1 bis 120 Monaten sein.';
    }
    months = m;
  }
  let maxMinus: number | null = null;
  if (form.limitMinus) {
    const m = toNumber(form.maxMinus);
    if (m == null || m < 0 || m > 1000 || !hasAtMostTwoDecimals(m)) {
      return 'Die Untergrenze muss zwischen 0 und 1000 Stunden liegen (höchstens zwei Nachkommastellen).';
    }
    maxMinus = m;
  }
  return { payoutThresholdHours: payout, expiryMonths: months, bookMinusHours: form.bookMinus, maxMinusHours: maxMinus };
}

export interface MonthlyThreshold {
  relationshipId: string;
  hours: number;
}

/** The monthly threshold in hours of a text field; `null` = remove it. A string is the plain-language problem. */
export function thresholdHours(text: string): number | null | string {
  const value = toNumber(text);
  if (value === undefined || (value !== null && (value < 0 || value > 744 || !hasAtMostTwoDecimals(value)))) {
    return 'Die Monatsschwelle muss zwischen 0 und 744 Stunden liegen (höchstens zwei Nachkommastellen).';
  }
  return value;
}

// ---------------------------------------------------------------- certificates (Krankmeldung und AU)

export interface CertificateRetention extends Pick<Audited, 'isDefault' | 'changedAt' | 'changedBy'> {
  retentionAfterEndDays: number;
}

export interface CertificateReminders extends Audited {
  firstReminderAfterReturnDays: number;
  reminderEveryDays: number;
  maxReminders: number;
}

export interface AccessLogEntry {
  id: string;
  at: string;
  actorId: string | null;
  action: string;
  absenceId: string;
  documentId: string;
  employeeId: string;
}

export interface Page<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

const ACTIONS: Record<string, string> = {
  Uploaded: 'Hochgeladen',
  Downloaded: 'Geöffnet',
  Verified: 'Bestätigt',
  Rejected: 'Abgelehnt',
  DeletedByUploader: 'Von der Person gelöscht',
  DeletedByPersonnel: 'Von der Personalabteilung gelöscht',
  PurgedByRetention: 'Nach Ablauf der Löschfrist entfernt',
  RetentionChanged: 'Löschfrist geändert',
};

export const accessActionLabel = (action: string): string => ACTIONS[action] ?? action;

/** A whole number in a range, or the plain-language problem. */
export function wholeNumber(text: string, min: number, max: number, what: string): number | string {
  const value = toNumber(text);
  if (value == null || !Number.isInteger(value) || value < min || value > max) {
    return `${what} muss eine ganze Zahl von ${min} bis ${max} sein.`;
  }
  return value;
}

// ---------------------------------------------------------------- employee card (Mitarbeiterkarte)

export interface NumberSeries {
  lastNumber: number | null;
  startNumber?: number | null;
  endNumber?: number | null;
  remaining?: number | null;
}

export interface SetNumberSeriesRequest {
  lastNumber: number;
  startNumber?: number;
  endNumber?: number;
}

export interface SeriesForm {
  last: string;
  start: string;
  end: string;
}

export const seriesForm = (series: NumberSeries): SeriesForm => ({
  last: series.lastNumber === null ? '' : String(series.lastNumber),
  start: series.startNumber == null ? '' : String(series.startNumber),
  end: series.endNumber == null ? '' : String(series.endNumber),
});

export function seriesRequest(form: SeriesForm, current: NumberSeries): SetNumberSeriesRequest | string {
  const last = toNumber(form.last);
  const start = toNumber(form.start);
  const end = toNumber(form.end);
  if (last == null || !Number.isInteger(last) || last < 0) {
    return 'Bitte die zuletzt vergebene Personalnummer als ganze Zahl eintragen.';
  }
  if (start === undefined || end === undefined || (start !== null && !Number.isInteger(start)) || (end !== null && !Number.isInteger(end))) {
    return 'Anfang und Ende des Bereichs müssen ganze Zahlen sein.';
  }
  const from = start ?? current.startNumber ?? 1001;
  const to = end ?? current.endNumber ?? 99999;
  if (from < 1 || to < from) {
    return 'Das Ende des Bereichs darf nicht vor dem Anfang liegen.';
  }
  if (last < from - 1 || last > to) {
    return 'Die zuletzt vergebene Nummer muss im Bereich liegen (höchstens eine unter dem Anfang).';
  }
  const request: SetNumberSeriesRequest = { lastNumber: last };
  if (start !== null) {
    request.startNumber = start;
  }
  if (end !== null) {
    request.endNumber = end;
  }
  return request;
}

export interface ReferenceEntry {
  id: string;
  kind: string;
  code: string;
  name: string;
  isActive: boolean;
}

/** The selection lists of the employee card; `kind` is the segment of `/api/employee-lists/{kind}`. */
export const LIST_KINDS = [
  { kind: 'Function', label: 'Funktion' },
  { kind: 'Role', label: 'Rolle' },
  { kind: 'Region', label: 'Region' },
  { kind: 'Workplace', label: 'Arbeitsort' },
  { kind: 'EmploymentRelationship', label: 'Arbeitsverhältnis' },
  { kind: 'EmploymentCode', label: 'Beschäftigungsverhältniscode' },
  { kind: 'AuthorizationGroup', label: 'Berechtigungsgruppe' },
  { kind: 'WageType', label: 'Lohnart' },
] as const;

// ---------------------------------------------------------------- unavailability kinds (Nicht-Verfügbarkeitsarten)

export interface UnavailabilityKind {
  id: string;
  code: string;
  description: string;
  paidTime: boolean;
  scheduleSymbol: string | null;
  workCode: string | null;
  isActive: boolean;
  isSystem: boolean;
}

export interface KindForm {
  code: string;
  description: string;
  paidTime: boolean;
  scheduleSymbol: string;
  workCode: string;
}

export const emptyKindForm = (): KindForm => ({ code: '', description: '', paidTime: false, scheduleSymbol: '', workCode: '' });

export const kindToForm = (k: UnavailabilityKind): KindForm => ({
  code: k.code,
  description: k.description,
  paidTime: k.paidTime,
  scheduleSymbol: k.scheduleSymbol ?? '',
  workCode: k.workCode ?? '',
});

// ---------------------------------------------------------------- holiday opening (Feiertage)

export interface StoreHolidayOpening {
  storeId: string;
  opensOnHolidays: boolean;
  changedAt: string | null;
  changedBy: string | null;
}

/** `open`/`closed` as set, `unknown` while loading, `blocked` when the store is outside the reach or not known to Workforce. */
export type HolidayState = 'open' | 'closed' | 'unknown' | 'blocked';

export const MAX_BULK_STORES = 500;
