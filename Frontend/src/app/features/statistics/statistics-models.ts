/** Rows of the report statistics of the portal. Months come as `yyyy-MM`; combinations without reports are 0, empty months/stores are missing. */
export interface MonthlyStatistic {
  month: string;
  employee: number;
  system: number;
  total: number;
}

export interface MonthlyCategoryStatistic {
  month: string;
  bug: number;
  virus: number;
  theft: number;
  other: number;
  suggestion: number;
  total: number;
}

export interface LocationSeverityStatistic {
  locationId: string;
  locationName: string;
  low: number;
  medium: number;
  high: number;
  critical: number;
  total: number;
}

export interface PersonnelNumberStatistic {
  personnelNumber: number;
  low: number;
  medium: number;
  high: number;
  critical: number;
  total: number;
}

/** The first and last month (`yyyy-MM`, both inclusive) of the evaluation; an empty one is open. */
export interface MonthRange {
  from: string;
  to: string;
}

const MONTHS = ['Januar', 'Februar', 'März', 'April', 'Mai', 'Juni', 'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'];

/** `2026-11` becomes `November 2026`; anything else is shown as it is. */
export function monthLabel(key: string): string {
  const match = /^(\d{4})-(\d{2})$/.exec(key);
  const name = match ? MONTHS[Number(match[2]) - 1] : undefined;
  return match && name ? `${name} ${match[1]}` : key;
}

/** Width of a bar in percent of the largest value; a value above 0 is always visible. */
export function barPercent(value: number, max: number): number {
  if (max <= 0 || value <= 0) {
    return 0;
  }
  return Math.max(2, Math.round((value / max) * 100));
}

export const maxOf = (values: number[]): number => values.reduce((a, b) => Math.max(a, b), 0);

/** The range is only valid if both months look like `yyyy-MM` (or are empty) and the first is not after the last. */
export function rangeProblem(range: MonthRange): string | null {
  const valid = (v: string) => v === '' || /^\d{4}-(0[1-9]|1[0-2])$/.test(v);
  if (!valid(range.from) || !valid(range.to)) {
    return 'Bitte gib die Monate als Monat und Jahr an.';
  }
  if (range.from && range.to && range.from > range.to) {
    return 'Der erste Monat darf nicht nach dem letzten Monat liegen.';
  }
  return null;
}
