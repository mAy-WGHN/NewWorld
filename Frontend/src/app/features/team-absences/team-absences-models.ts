import { errorMessage as baseErrorMessage, formatDate } from '../absences/absence-models';

export { formatDate };

/**
 * A period in which a person is not available, as the viewer may see it (`UnavailabilityDto` of Workforce). Dates are ISO dates, the open end
 * (`to` = `null`) is a sick note without an end. `kind` is `null` when the viewer only sees "nicht verfügbar"; then the other fields are empty, too.
 */
export interface UnavailabilityPeriod {
  employeeId: string;
  from: string;
  to: string | null;
  status: string;
  id?: string | null;
  kind?: string | null;
  description?: string | null;
  scheduleSymbol?: string | null;
  paidTime?: boolean | null;
  /** `Absence`, `Manual` or `System`; only in the full view. */
  origin?: string | null;
  /** Only in the full view: the period reaches past the exit date (a hint, nothing is changed). */
  afterExit?: boolean | null;
}

/** One row of `GET /api/stores/{id}/unavailabilities`. */
export interface StoreUnavailability {
  employeeName: string;
  period: UnavailabilityPeriod;
}

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

export interface AddUnavailabilityInput {
  kindId: string;
  from: string;
  to: string;
}

/** A store the person may choose (id as in Portal and Workforce). */
export interface StoreOption {
  id: string;
  name: string;
}

export interface TeamMember {
  employeeId: string;
  name: string;
}

/** An active person of a store (`GET /api/stores/{id}/people`), also one without an entry. */
export interface StorePerson {
  employeeId: string;
  name: string;
}

export type CellKind = 'sick' | 'vacation' | 'ku' | 'other' | 'unavailable';

export interface DayCell {
  kind: CellKind;
  label: string;
  afterExit: boolean;
}

export const KIND_SICKNESS = 'KRANK';
export const KIND_VACATION = 'URLAUB';
export const KIND_SICK_IN_VACATION = 'KRANK I URLAUB';

/** Legend and tooltips. */
export const CELL_LABELS: Record<CellKind, string> = {
  sick: 'Krank',
  vacation: 'Urlaub',
  ku: 'Krank im Urlaub',
  other: 'Sonstige Abwesenheit',
  unavailable: 'Nicht verfügbar',
};

export const CELL_KINDS: readonly CellKind[] = ['sick', 'vacation', 'ku', 'other', 'unavailable'];

/** Which kind wins if two periods cover the same day (KU shows what the schedule shows anyway). */
const PRIORITY: Record<CellKind, number> = { ku: 4, sick: 3, vacation: 2, other: 1, unavailable: 0 };

export function cellKindOf(period: UnavailabilityPeriod): CellKind {
  switch (period.kind) {
    case KIND_SICKNESS:
      return 'sick';
    case KIND_VACATION:
      return 'vacation';
    case KIND_SICK_IN_VACATION:
      return 'ku';
    case null:
    case undefined:
      return 'unavailable';
    default:
      return 'other';
  }
}

// ---- Dates: ISO texts and UTC only, so no time zone and no daylight saving change can shift a day. ----

export interface YearMonth {
  year: number;
  month: number;
}

export const MONTH_NAMES = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];

const pad = (n: number): string => String(n).padStart(2, '0');

export const isoDate = (year: number, month: number, day: number): string => `${year}-${pad(month)}-${pad(day)}`;

/** Today as an ISO date in local time (the day the person sees on the wall). */
export function todayIso(now: Date = new Date()): string {
  return isoDate(now.getFullYear(), now.getMonth() + 1, now.getDate());
}

export function monthOf(iso: string): YearMonth {
  const [year, month] = iso.split('-').map(Number);
  return { year, month };
}

export function shiftMonth(value: YearMonth, delta: number): YearMonth {
  const index = value.year * 12 + (value.month - 1) + delta;
  return { year: Math.floor(index / 12), month: (index % 12) + 1 };
}

export const daysInMonth = (value: YearMonth): number => new Date(Date.UTC(value.year, value.month, 0)).getUTCDate();

export const monthLabel = (value: YearMonth): string => `${MONTH_NAMES[value.month - 1]} ${value.year}`;

export const monthStart = (value: YearMonth): string => isoDate(value.year, value.month, 1);

export const monthEnd = (value: YearMonth): string => isoDate(value.year, value.month, daysInMonth(value));

export interface DayColumn {
  iso: string;
  day: number;
  /** Two letters, Monday-based German short name. */
  weekday: string;
  weekend: boolean;
}

const WEEKDAYS = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

const columnOf = (year: number, month: number, day: number): DayColumn => {
  const weekday = new Date(Date.UTC(year, month - 1, day)).getUTCDay();
  return { iso: isoDate(year, month, day), day, weekday: WEEKDAYS[weekday], weekend: weekday === 0 || weekday === 6 };
};

export function monthDays(value: YearMonth): DayColumn[] {
  return Array.from({ length: daysInMonth(value) }, (_, i) => columnOf(value.year, value.month, i + 1));
}

/** The days from `from` to `to` (both included, ISO dates, UTC only) as columns of a grid. */
export function dayColumns(from: string, to: string): DayColumn[] {
  const columns: DayColumn[] = [];
  const [y, m, d] = from.split('-').map(Number);
  for (let date = new Date(Date.UTC(y, m - 1, d)); date.toISOString().slice(0, 10) <= to; date = new Date(date.getTime() + 86_400_000)) {
    columns.push(columnOf(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate()));
  }
  return columns;
}

/** Colour from the theme, plus a letter in the cell so the colour is never the only signal. */
export const CELL_CLASS: Record<CellKind, string> = {
  sick: 'bg-error text-on-error',
  vacation: 'bg-primary text-on-primary',
  ku: 'bg-error-container text-on-error-container',
  other: 'bg-tile-orange text-on-surface',
  unavailable: 'bg-outline-variant text-on-surface',
};

export const CELL_TEXT: Record<CellKind, string> = { sick: 'K', vacation: 'U', ku: 'KU', other: 'S', unavailable: 'n' };

/** The people of a store that appear in the answer (the service only lists people with a period), each once, sorted by name. */
export function membersOf(rows: readonly StoreUnavailability[]): TeamMember[] {
  const byId = new Map<string, TeamMember>();
  for (const row of rows) {
    if (!byId.has(row.period.employeeId)) {
      byId.set(row.period.employeeId, { employeeId: row.period.employeeId, name: row.employeeName });
    }
  }
  return [...byId.values()].sort((a, b) => a.name.localeCompare(b.name, 'de'));
}

/** All active people of the store plus those that appear in the rows (for example somebody who left but still has a period), each once, sorted by name. */
export function membersWithPeople(people: readonly StorePerson[], rows: readonly StoreUnavailability[]): TeamMember[] {
  const byId = new Map<string, TeamMember>(people.map((p) => [p.employeeId, { employeeId: p.employeeId, name: p.name }]));
  for (const member of membersOf(rows)) {
    if (!byId.has(member.employeeId)) {
      byId.set(member.employeeId, member);
    }
  }
  return [...byId.values()].sort((a, b) => a.name.localeCompare(b.name, 'de'));
}

const covers = (period: UnavailabilityPeriod, iso: string): boolean => period.from <= iso && (period.to === null || iso <= period.to);

/** The cells of one person for the days of a month (`null` = free). */
export function cellsFor(periods: readonly UnavailabilityPeriod[], days: readonly DayColumn[]): (DayCell | null)[] {
  return days.map((d) => {
    let best: { cell: DayCell; priority: number } | null = null;
    for (const p of periods) {
      if (!covers(p, d.iso)) {
        continue;
      }
      const kind = cellKindOf(p);
      if (!best || PRIORITY[kind] > best.priority) {
        const label = kind === 'other' ? p.description || p.kind || CELL_LABELS.other : CELL_LABELS[kind];
        best = { cell: { kind, label, afterExit: p.afterExit === true }, priority: PRIORITY[kind] };
      }
    }
    return best?.cell ?? null;
  });
}

/** The text of a period for lists: its kind, if the viewer may see one. */
export const periodLabel = (p: UnavailabilityPeriod): string => {
  const kind = cellKindOf(p);
  return kind === 'other' ? p.description || p.kind || CELL_LABELS.other : CELL_LABELS[kind];
};

/** Entries HR entered by hand: only those can be deleted (the others come from absences or the system). */
export const isManualEntry = (p: UnavailabilityPeriod): boolean => p.origin === 'Manual' && !!p.id;

/** The kinds HR may enter: active, not one of the system kinds (sickness, vacation and KU come from the absences). */
export const enterableKinds = (kinds: readonly UnavailabilityKind[]): UnavailabilityKind[] => kinds.filter((k) => k.isActive && !k.isSystem);

/** The message for a wrong period, or `null` if it is fine. ISO dates compare correctly as text. */
export function validateEntry(input: { kindId: string; from: string; to: string }): string | null {
  if (!input.kindId) {
    return 'Bitte eine Art wählen.';
  }
  if (!input.from) {
    return 'Bitte einen Beginn angeben.';
  }
  if (!input.to) {
    return 'Bitte ein Ende angeben.';
  }
  return input.to < input.from ? 'Das Ende liegt vor dem Beginn.' : null;
}

const ERRORS: Record<string, string> = {
  InvalidPeriod: 'Der Zeitraum ist nicht gültig.',
  BeforeEntry: 'Der Zeitraum beginnt vor dem Eintritt der Person.',
  AfterExit: 'Der Zeitraum liegt nach dem Austritt der Person.',
  Overlap: 'Der Zeitraum überschneidet sich mit einem anderen Eintrag oder einer Abwesenheit.',
  UnknownKind: 'Diese Art gibt es nicht.',
  KindInactive: 'Diese Art ist nicht mehr aktiv.',
  KindNotEnterable: 'Krank und Urlaub kommen aus den Abwesenheiten und können nicht von Hand eingetragen werden.',
  SystemEntry: 'Dieser Eintrag folgt einer Krankmeldung und kann nicht von Hand gelöscht werden.',
  UnknownPerson: 'Die Person wurde nicht gefunden.',
  NotFound: 'Der Eintrag wurde nicht gefunden.',
};

export const entryErrorMessage = (title: string | undefined): string => (title && ERRORS[title]) || baseErrorMessage(undefined);
