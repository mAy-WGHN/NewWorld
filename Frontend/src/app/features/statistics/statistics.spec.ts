import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatisticsPage } from './statistics.page';
import { StatisticsApi } from './statistics.api';
import { barPercent, monthLabel, rangeProblem } from './statistics-models';

describe('statistics-models', () => {
  it('names months in German', () => {
    expect(monthLabel('2026-11')).toBe('November 2026');
    expect(monthLabel('2026-03')).toBe('März 2026');
    expect(monthLabel('kaputt')).toBe('kaputt');
    expect(monthLabel('2026-13')).toBe('2026-13');
  });

  it('scales bars to the largest value and keeps small values visible', () => {
    expect(barPercent(10, 10)).toBe(100);
    expect(barPercent(5, 10)).toBe(50);
    expect(barPercent(1, 1000)).toBe(2);
    expect(barPercent(0, 10)).toBe(0);
    expect(barPercent(3, 0)).toBe(0);
  });

  it('accepts empty and ordered ranges and refuses the others', () => {
    expect(rangeProblem({ from: '', to: '' })).toBeNull();
    expect(rangeProblem({ from: '2026-01', to: '2026-01' })).toBeNull();
    expect(rangeProblem({ from: '2026-05', to: '2026-01' })).toContain('nicht nach');
    expect(rangeProblem({ from: '2026-1', to: '' })).toContain('Monat');
  });
});

describe('StatisticsApi', () => {
  let api: StatisticsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(StatisticsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks without parameters for all months', async () => {
    const done = api.monthly({ from: '', to: '' });
    const request = http.expectOne('/svc/portal/api/statistics/monthly');
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ success: true, data: [{ month: '2026-10', employee: 1, system: 0, total: 1 }] });

    expect(await done).toHaveLength(1);
  });

  it('sends the months as from and to to every evaluation', async () => {
    const range = { from: '2026-01', to: '2026-03' };
    const calls = [
      api.monthlyByCategory(range),
      api.byLocationSeverity(range),
      api.byPersonnelNumber(range),
    ];
    for (const name of ['monthly-by-category', 'by-location-severity', 'by-personnel-number']) {
      const request = http.expectOne((r) => r.url === `/svc/portal/api/statistics/${name}`);
      expect(request.request.params.get('from')).toBe('2026-01');
      expect(request.request.params.get('to')).toBe('2026-03');
      request.flush({ success: true, data: [] });
    }
    await Promise.all(calls);
  });
});

describe('StatisticsPage', () => {
  let api: Record<'monthly' | 'monthlyByCategory' | 'byLocationSeverity' | 'byPersonnelNumber', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<StatisticsPage>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: StatisticsApi, useValue: api }] });
    fixture = TestBed.createComponent(StatisticsPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const rows = (testId: string) => [...root.querySelectorAll(`[data-testid="${testId}"]`)];

  beforeEach(() => {
    api = {
      monthly: vi.fn().mockResolvedValue([
        { month: '2026-09', employee: 2, system: 0, total: 2 },
        { month: '2026-10', employee: 3, system: 5, total: 8 },
      ]),
      monthlyByCategory: vi.fn().mockResolvedValue([{ month: '2026-10', bug: 1, virus: 2, theft: 3, other: 4, suggestion: 5, total: 15 }]),
      byLocationSeverity: vi.fn().mockResolvedValue([{ locationId: 'l1', locationName: 'Augsburg', low: 1, medium: 0, high: 0, critical: 2, total: 3 }]),
      byPersonnelNumber: vi.fn().mockResolvedValue([{ personnelNumber: 4711, low: 0, medium: 1, high: 0, critical: 0, total: 1 }]),
    };
  });

  it('shows the four evaluations as tables with all numbers', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Statistik');
    expect(rows('monthly-row')).toHaveLength(2);
    expect(rows('monthly-row')[1].textContent).toContain('Oktober 2026');
    expect(rows('monthly-row')[1].textContent).toContain('8');
    expect(rows('category-row')[0].textContent).toContain('15');
    expect(rows('location-row')[0].textContent).toContain('Augsburg');
    expect(rows('person-row')[0].textContent).toContain('4711');
  });

  it('draws bars relative to the largest value and hides them from screen readers', async () => {
    await create();

    const bars = rows('monthly-row').map((r) => r.querySelector<HTMLElement>('td[aria-hidden="true"] div')!.style.width);
    expect(bars).toEqual(['25%', '100%']);
  });

  it('asks for the chosen months', async () => {
    await create();
    api['monthly'].mockClear();

    for (const [selector, value] of [['[data-testid="from"]', '2026-01'], ['[data-testid="to"]', '2026-03']]) {
      const input = q<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    await settle();
    q<HTMLButtonElement>('[data-testid="apply"]')!.click();
    await settle();

    expect(api['monthly']).toHaveBeenCalledWith({ from: '2026-01', to: '2026-03' });
    expect(api['byPersonnelNumber']).toHaveBeenCalledWith({ from: '2026-01', to: '2026-03' });
  });

  it('does not ask when the first month is after the last', async () => {
    await create();
    api['monthly'].mockClear();

    for (const [selector, value] of [['[data-testid="from"]', '2026-05'], ['[data-testid="to"]', '2026-01']]) {
      const input = q<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    await settle();
    q<HTMLButtonElement>('[data-testid="apply"]')!.click();
    await settle();

    expect(api['monthly']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('nicht nach');
  });

  it('says so when there are no reports', async () => {
    for (const fn of Object.values(api)) {
      fn.mockResolvedValue([]);
    }
    await create();

    expect(rows('monthly-row')).toHaveLength(0);
    expect(q('[data-testid="empty"]')!.textContent).toContain('keine Meldungen');
  });

  it('shows the answer of the portal when the person may not read the statistics', async () => {
    api['monthly'].mockRejectedValue(new HttpErrorResponse({ status: 403, error: { success: false, message: 'Dafür fehlt dir das Recht.', errors: [] } }));
    await create();

    expect(q('[data-testid="error"]')!.textContent).toContain('Dafür fehlt dir das Recht.');
    expect(rows('monthly-row')).toHaveLength(0);
  });
});
