import { Absence } from '../absences/absence-models';
import { YearMonth, daysInMonth, isoDate } from '../team-absences/team-absences-models';

export type VacationShown = 'approved' | 'requested' | 'rejected';

/** Only vacation that counts is drawn; withdrawn and cancelled requests are not. */
export function vacationStatusOf(status: string): VacationShown | null {
  switch (status) {
    case 'Approved':
      return 'approved';
    case 'Requested':
      return 'requested';
    case 'Rejected':
      return 'rejected';
    default:
      return null;
  }
}

/** Which status wins when requests overlap on a day. */
const RANK: Record<VacationShown, number> = { approved: 3, requested: 2, rejected: 1 };

export type ShownVacation = Absence & { shown: VacationShown };

/** A statutory holiday of the person's store (`GET /api/me/holidays`); `storeOpens`: the store is open, so it counts as a working day. */
export interface Holiday {
  date: string;
  name: string;
  storeOpens: boolean;
}

export interface CalendarDay {
  iso: string;
  day: number;
  inMonth: boolean;
  weekend: boolean;
  today: boolean;
  /** The vacation request that covers the day (the strongest if several do), with the status shown. */
  vacation: ShownVacation | null;
  holiday: Holiday | null;
}

const addDays = (iso: string, delta: number): string => {
  const [y, m, d] = iso.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d + delta));
  return isoDate(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate());
};

/** Monday-based weeks (full weeks of 7 days) covering the month; ISO texts and UTC only, so no time zone can shift a day. */
export function calendarWeeks(month: YearMonth, absences: readonly Absence[], today: string, holidays: readonly Holiday[] = []): CalendarDay[][] {
  const holidayByDate = new Map(holidays.map((h) => [h.date, h]));
  const length = daysInMonth(month);
  const first = isoDate(month.year, month.month, 1);
  const last = isoDate(month.year, month.month, length);
  const lead = (new Date(Date.UTC(month.year, month.month - 1, 1)).getUTCDay() + 6) % 7; // Monday = 0
  const trail = 6 - ((new Date(Date.UTC(month.year, month.month - 1, length)).getUTCDay() + 6) % 7);

  const covering: ShownVacation[] = [];
  for (const a of absences) {
    const shown = a.type === 'Vacation' ? vacationStatusOf(a.status) : null;
    if (shown) {
      covering.push({ ...a, shown });
    }
  }

  const days: CalendarDay[] = [];
  for (let iso = addDays(first, -lead); iso <= addDays(last, trail); iso = addDays(iso, 1)) {
    let best: ShownVacation | null = null;
    for (const v of covering) {
      if (v.from <= iso && iso <= (v.to ?? v.from) && (best === null || RANK[v.shown] > RANK[best.shown])) {
        best = v;
      }
    }
    const weekday = new Date(`${iso}T00:00:00Z`).getUTCDay();
    days.push({
      iso,
      day: Number(iso.slice(8)),
      inMonth: iso >= first && iso <= last,
      weekend: weekday === 0 || weekday === 6,
      today: iso === today,
      vacation: best,
      holiday: holidayByDate.get(iso) ?? null,
    });
  }

  return Array.from({ length: days.length / 7 }, (_, i) => days.slice(i * 7, i * 7 + 7));
}

/** The period between two days picked in any order (dragging backwards works). ISO dates compare correctly as text. */
export function selectionRange(a: string, b: string): { from: string; to: string } {
  return a <= b ? { from: a, to: b } : { from: b, to: a };
}

export const inRange = (iso: string, range: { from: string; to: string } | null): boolean => !!range && range.from <= iso && iso <= range.to;
