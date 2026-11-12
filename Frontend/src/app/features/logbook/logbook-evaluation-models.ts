/** The figures of a store, a region or a district for a year (`GET /api/logbook/evaluation`). */
export interface Figures {
  submitted: number;
  onTime: number;
  late: number;
  missing: number;
  /** What was handed in plus what is overdue and missing: the rate is counted against it. */
  expected: number;
  /** 0 to 1; null if nothing was expected. */
  onTimeRate: number | null;
  /** 1 to 5; null if no report has a rating. */
  ratingAverage: number | null;
  ratedCount: number;
  /** How many reports have the rating 1, 2, 3, 4 and 5, in this order. */
  distribution: number[];
  escalated: number;
  answered: number;
  done: number;
}

export interface StoreFigures {
  storeId: string;
  storeNumber: string;
  storeName: string;
  districtId: string;
  regionId: string | null;
  figures: Figures;
}

export interface GroupFigures {
  id: string;
  name: string;
  stores: number;
  figures: Figures;
}

export interface Evaluation {
  year: number;
  asOf: string;
  total: Figures;
  stores: StoreFigures[];
  regions: GroupFigures[];
  districts: GroupFigures[];
}

/** The years to choose from: this year and the five before, never before the first year of the service. */
export const FIRST_YEAR = 2020;

export function evaluationYears(thisYear: number): number[] {
  const years: number[] = [];
  for (let year = thisYear; year >= Math.max(FIRST_YEAR, thisYear - 5); year--) {
    years.push(year);
  }
  return years;
}

/** "40 %"; a dash if there is nothing to say. */
export function formatRate(rate: number | null): string {
  return rate === null ? '–' : `${Math.round(rate * 100)} %`;
}

/** "4,0": one decimal, a comma; a dash if there is no rating. */
export function formatAverage(average: number | null): string {
  return average === null ? '–' : average.toFixed(1).replace('.', ',');
}

/** The spread of the ratings in words, for those who do not see the bars. */
export function distributionLabel(distribution: readonly number[]): string {
  return `Bewertungen: ${distribution.map((count, i) => `${i + 1}: ${count}`).join(', ')}`;
}

/** The height of each bar as a share of the highest one (0 to 100). */
export function barHeights(distribution: readonly number[]): number[] {
  const highest = Math.max(0, ...distribution);
  return distribution.map((count) => (highest === 0 ? 0 : Math.round((count / highest) * 100)));
}
