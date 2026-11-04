export interface AccountBucket {
  earnedOn: string;
  remaining: number;
  expiresOn: string | null;
}

export interface TimeBooking {
  id: string;
  kind: string;
  hours: number;
  date: string;
  monthCloseId: string | null;
  createdAt: string;
}

export interface MonthClose {
  id: string;
  year: number;
  month: number;
  workedHours: number;
  thresholdHours: number;
  overtimeHours: number;
  closedAt: string;
  minusHours: number;
  minusHoursNotBooked: number;
}

/** The working time account of the signed-in person (`GET /api/me/working-time`). Dates are ISO dates. */
export interface WorkingTime {
  balance: number;
  deficit: number;
  forfeited: number;
  buckets: AccountBucket[];
  bookings: TimeBooking[];
  closes: MonthClose[];
}

const KINDS: Record<string, string> = {
  OpeningBalance: 'Anfangsbestand',
  Overtime: 'Überstunden',
  TimeOff: 'Freizeitausgleich',
  Payout: 'Auszahlung',
  PayoutAtExit: 'Auszahlung bei Austritt',
  Correction: 'Korrektur',
  MinusHours: 'Minusstunden',
};

const MONTHS = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];

/** An unknown kind is shown as it is, so a new kind of booking never disappears silently. */
export const kindLabel = (kind: string): string => KINDS[kind] ?? kind;

export const monthLabel = (year: number, month: number): string => `${MONTHS[month - 1]} ${year}`;

/** Hours as `+2,5 h` / `−1 h` (German comma, at most two decimals); without the sign only a minus for negative values. */
export function formatHours(hours: number, withSign = true): string {
  const rounded = Math.round(hours * 100) / 100;
  const text = Math.abs(rounded).toLocaleString('de-DE', { maximumFractionDigits: 2 });
  if (rounded < 0) {
    return `−${text} h`;
  }
  return rounded > 0 && withSign ? `+${text} h` : `${text} h`;
}

/** An ISO date moved by whole days (UTC, so no summer time can shift it). */
export function addDays(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** Hours that run out between today and `days` days from now (both days included), soonest first. */
export function expiringSoon(buckets: readonly AccountBucket[], today: string, days: number): AccountBucket[] {
  const until = addDays(today, days);
  return buckets
    .filter((b) => b.remaining > 0 && b.expiresOn !== null && b.expiresOn >= today && b.expiresOn <= until)
    .sort((a, b) => a.expiresOn!.localeCompare(b.expiresOn!));
}
