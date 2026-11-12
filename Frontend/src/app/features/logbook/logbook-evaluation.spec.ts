import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LogbookEvaluationView } from './logbook-evaluation';
import { Evaluation, FIRST_YEAR, Figures, barHeights, distributionLabel, evaluationYears, formatAverage, formatRate } from './logbook-evaluation-models';
import { LogbookApi } from './logbook.api';

const figures = (patch: Partial<Figures> = {}): Figures => ({
  submitted: 3, onTime: 2, late: 1, missing: 1, expected: 4, onTimeRate: 0.5, ratingAverage: 3.5, ratedCount: 2, distribution: [0, 1, 0, 1, 0], escalated: 1, answered: 2, done: 1, ...patch,
});

describe('formatting of the evaluation', () => {
  it('writes a rate as a whole percent and a dash for nothing', () => {
    expect(formatRate(0.5)).toBe('50 %');
    expect(formatRate(1)).toBe('100 %');
    expect(formatRate(0)).toBe('0 %');
    expect(formatRate(1 / 3)).toBe('33 %');
    expect(formatRate(2 / 3)).toBe('67 %');
    expect(formatRate(null)).toBe('–');
  });

  it('writes an average with one decimal and a comma, and a dash if there is no rating', () => {
    expect(formatAverage(4)).toBe('4,0');
    expect(formatAverage(3.456)).toBe('3,5');
    expect(formatAverage(1)).toBe('1,0');
    expect(formatAverage(null)).toBe('–');
  });

  it('describes the spread of the ratings in words', () => {
    expect(distributionLabel([0, 1, 0, 2, 3])).toBe('Bewertungen: 1: 0, 2: 1, 3: 0, 4: 2, 5: 3');
  });

  it('draws the bars in proportion to the highest one, and none if nothing was rated', () => {
    expect(barHeights([0, 2, 4, 1, 0])).toEqual([0, 50, 100, 25, 0]);
    expect(barHeights([0, 0, 0, 0, 0])).toEqual([0, 0, 0, 0, 0]);
  });

  it('offers this year and the five before, never before the first year', () => {
    expect(evaluationYears(2026)).toEqual([2026, 2025, 2024, 2023, 2022, 2021]);
    expect(evaluationYears(FIRST_YEAR + 1)).toEqual([FIRST_YEAR + 1, FIRST_YEAR]);
  });
});

describe('LogbookEvaluationView', () => {
  const evaluation = (patch: Partial<Evaluation> = {}): Evaluation => ({
    year: new Date().getFullYear(),
    asOf: new Date(2026, 9, 23, 17, 0).toISOString(),
    total: figures(),
    stores: [
      { storeId: 'a', storeNumber: '001', storeName: 'Augsburg', districtId: 'd1', regionId: 'r1', figures: figures({ onTimeRate: 1, ratingAverage: 5, distribution: [0, 0, 0, 0, 1] }) },
      { storeId: 'c', storeNumber: '003', storeName: 'Ulm', districtId: 'd1', regionId: null, figures: figures({ onTimeRate: 0, ratingAverage: null, distribution: [0, 0, 0, 0, 0], submitted: 0, ratedCount: 0 }) },
    ],
    regions: [{ id: 'r1', name: 'Augsburg', stores: 2, figures: figures() }],
    districts: [{ id: 'd1', name: 'Süd', stores: 3, figures: figures({ onTimeRate: 0.4 }) }],
    ...patch,
  });

  let api: { evaluation: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<LogbookEvaluationView>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(data: Evaluation = evaluation()): Promise<void> {
    api.evaluation.mockResolvedValue(data);
    TestBed.configureTestingModule({ providers: [{ provide: LogbookApi, useValue: api }] });
    fixture = TestBed.createComponent(LogbookEvaluationView);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const rowTexts = (section: string, col: string): string[] => [...root.querySelectorAll(`[data-testid="${section}"] [data-col="${col}"]`)].map((c) => c.textContent!.trim());

  beforeEach(() => {
    api = { evaluation: vi.fn() };
  });

  it('asks for this year, and shows the summary with the rate, the average and what was handed in', async () => {
    await create();

    expect(api.evaluation).toHaveBeenCalledWith(new Date().getFullYear());
    expect(q('[data-testid="total-rate"]')!.textContent).toContain('50 %');
    expect(q('[data-testid="summary"]')!.textContent).toContain('2 von 4 erwartet');
    expect(q('[data-testid="total-average"]')!.textContent).toContain('3,5');
    expect(q('[data-testid="summary"]')!.textContent).toContain('aus 2 Berichten');
    expect(q('[data-testid="total-submitted"]')!.textContent).toContain('3');
    expect(q('[data-testid="summary"]')!.textContent).toContain('1 verspätet, 1 fehlend');
    expect(q('[data-testid="total-done"]')!.textContent).toContain('1 erledigt');
    expect(q('[data-testid="summary"]')!.textContent).toContain('2 beantwortet, 1 eskaliert');
    expect(q('[data-testid="as-of"]')!.textContent).toContain('23.10.2026, 17:00 Uhr');
  });

  it('shows districts, regions and stores as tables with the figures of every row', async () => {
    await create();

    expect(rowTexts('districts', 'rate')).toEqual(['40 %']);
    expect(root.querySelector('[data-testid="districts"]')!.textContent).toContain('3 Filialen');
    expect(rowTexts('regions', 'rate')).toEqual(['50 %']);
    expect(rowTexts('stores', 'rate')).toEqual(['100 %', '0 %']);
    expect(rowTexts('stores', 'average')).toEqual(['5,0', '–']); // no rating is a dash, not 0,0
    expect(rowTexts('stores', 'submitted')).toEqual(['3', '0']);
    expect(rowTexts('stores', 'late')).toEqual(['1', '1']);
    expect(rowTexts('stores', 'missing')).toEqual(['1', '1']);
    expect(rowTexts('stores', 'escalated')).toEqual(['1', '1']);
    expect(rowTexts('stores', 'done')).toEqual(['1', '1']);
    expect([...root.querySelectorAll('[data-testid="stores"] [data-testid="row"] th')].map((h) => h.textContent!.replace(/\s+/g, ' ').trim())).toEqual(['001 Augsburg', '003 Ulm']);
  });

  it('describes the spread of the ratings for those who do not see the bars', async () => {
    await create();

    const spreads = [...root.querySelectorAll('[data-testid="stores"] [role="img"]')].map((e) => e.getAttribute('aria-label'));
    expect(spreads).toEqual(['Bewertungen: 1: 0, 2: 0, 3: 0, 4: 0, 5: 1', 'Bewertungen: 1: 0, 2: 0, 3: 0, 4: 0, 5: 0']);
    expect(root.querySelectorAll('[data-testid="stores"] [data-testid="row"]:first-child [role="img"] > span').length).toBe(5);
  });

  it('shows only what the service sent: a store manager gets stores but no region or district tables', async () => {
    await create(evaluation({ regions: [], districts: [] }));

    expect(q('[data-testid="districts"]')).toBeNull();
    expect(q('[data-testid="regions"]')).toBeNull();
    expect(q('[data-testid="stores"]')).not.toBeNull();
  });

  it('shows a dash instead of a rate when nothing was expected', async () => {
    await create(evaluation({ total: figures({ onTimeRate: null, ratingAverage: null, expected: 0, onTime: 0, ratedCount: 0 }) }));

    expect(q('[data-testid="total-rate"]')!.textContent).toContain('–');
    expect(q('[data-testid="total-average"]')!.textContent).toContain('–');
  });

  it('asks again for the year that is chosen', async () => {
    await create();
    const select = q<HTMLSelectElement>('[data-testid="year"]')!;
    const lastYear = new Date().getFullYear() - 1;

    select.value = String(lastYear);
    select.dispatchEvent(new Event('change'));
    await settle();

    expect(api.evaluation).toHaveBeenLastCalledWith(lastYear);
    expect([...select.options].map((o) => o.value)).toEqual(evaluationYears(new Date().getFullYear()).map(String));
  });

  it('explains what on time and missing mean and that only the figures stay after a year', async () => {
    await create();

    const text = q('[data-testid="explanation"]')!.textContent!;
    expect(text).toContain('Pünktlich heißt');
    expect(text).toContain('fehlend');
    expect(text).toContain('ein Jahr aufbewahrt');
  });

  it('shows the error of the service and no tables, for example for somebody who sees no store', async () => {
    api.evaluation.mockRejectedValue(new HttpErrorResponse({ status: 403, error: { success: false, message: 'Dafür fehlt dir das Recht.', errors: [] } }));
    TestBed.configureTestingModule({ providers: [{ provide: LogbookApi, useValue: api }] });
    fixture = TestBed.createComponent(LogbookEvaluationView);
    root = fixture.nativeElement as HTMLElement;
    await settle();

    expect(q('[data-testid="error"]')!.textContent).toContain('Recht');
    expect(q('[data-testid="stores"]')).toBeNull();
  });
});
