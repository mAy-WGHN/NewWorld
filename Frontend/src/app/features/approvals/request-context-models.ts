import { addDays } from '../vacation/vacation-calendar-models';
import { DayColumn, StoreUnavailability, UnavailabilityPeriod, dayColumns } from '../team-absences/team-absences-models';

/** Days of context shown before and after the requested period. */
export const CONTEXT_DAYS = 3;

/** The longest window shown (a long request shows its first weeks; the rest is not needed to see who else is away). */
export const MAX_WINDOW_DAYS = 62;

/** The days to show for a request: the period with a few days around it, at most {@link MAX_WINDOW_DAYS}. ISO dates. */
export function contextWindow(from: string, to: string): { from: string; to: string } {
  const start = addDays(from, -CONTEXT_DAYS);
  const end = addDays(to, CONTEXT_DAYS);
  const last = addDays(start, MAX_WINDOW_DAYS - 1);
  return { from: start, to: end < last ? end : last };
}

const covers = (p: UnavailabilityPeriod, iso: string): boolean => p.from <= iso && (p.to === null || iso <= p.to);

/** The window as day columns (for the grid). */
export const windowDays = (from: string, to: string): DayColumn[] => {
  const w = contextWindow(from, to);
  return dayColumns(w.from, w.to);
};

export interface OthersAbsent {
  /** The most colleagues away on one day of the request. */
  most: number;
  /** The days of the request on which at least one colleague is away. */
  days: number;
  /** The colleagues (by name) who are away on at least one day of the request. */
  names: string[];
}

/** Who else is away during the requested period; the applicant is not counted, and a person counts once per day however many periods cover it. */
export function othersAbsent(rows: readonly StoreUnavailability[], applicantId: string, from: string, to: string): OthersAbsent {
  const others = rows.filter((r) => r.period.employeeId !== applicantId);
  const names = new Set<string>();
  let most = 0;
  let days = 0;
  for (let iso = from; iso <= to; iso = addDays(iso, 1)) {
    const away = new Set(others.filter((r) => covers(r.period, iso)).map((r) => r.period.employeeId));
    if (away.size > 0) {
      days++;
      most = Math.max(most, away.size);
      for (const r of others) {
        if (covers(r.period, iso)) {
          names.add(r.employeeName);
        }
      }
    }
  }
  return { most, days, names: [...names].sort((a, b) => a.localeCompare(b, 'de')) };
}

/** The sentence under the grid. */
export function othersSummary(o: OthersAbsent): string {
  if (o.most === 0) {
    return 'In diesem Zeitraum fehlt sonst niemand in der Filiale.';
  }
  if (o.names.length === 1) {
    return `${o.names[0]} fehlt in diesem Zeitraum an ${o.days} ${o.days === 1 ? 'Tag' : 'Tagen'}.`;
  }
  return `${o.names.length} Personen fehlen in diesem Zeitraum (${o.names.join(', ')}), gleichzeitig höchstens ${o.most}.`;
}
