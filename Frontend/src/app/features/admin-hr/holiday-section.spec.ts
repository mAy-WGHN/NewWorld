import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminHrApi, StoreLocation } from './admin-hr.api';
import { HolidaySection, STATE_CONCURRENCY } from './holiday-section';

const store = (id: string, name: string, storeCode: string, archived = false): StoreLocation => ({ id, name, storeCode, archived });
const opening = (storeId: string, opensOnHolidays: boolean) => ({ storeId, opensOnHolidays, changedAt: null, changedBy: null });

type Methods = 'stores' | 'holidayOpening' | 'setHolidayOpening';

describe('HolidaySection', () => {
  let api: Record<Methods, ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<HolidaySection>;
  let root: HTMLElement;
  let open: Set<string>;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminHrApi, useValue: api }] });
    fixture = TestBed.createComponent(HolidaySection);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const rowOf = (id: string) => root.querySelector<HTMLElement>(`[data-testid="store-row"][data-id="${id}"]`)!;
  const stateOf = (id: string) => rowOf(id).querySelector('[data-testid="store-state"]')!.textContent!.trim();
  const check = (id: string) => rowOf(id).querySelector<HTMLInputElement>('[data-testid="store-check"]')!;
  const names = () => [...root.querySelectorAll('[data-testid="store-row"] strong')].map((e) => e.textContent);

  async function tick(selector: string): Promise<void> {
    check(selector).click();
    await settle();
  }

  async function click(selector: string): Promise<void> {
    q<HTMLElement>(selector)!.click();
    await settle();
  }

  async function search(value: string): Promise<void> {
    const input = q<HTMLInputElement>('[data-testid="search"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  beforeEach(() => {
    open = new Set(['s2']);
    api = {
      stores: vi.fn().mockResolvedValue([
        store('s1', 'Augsburg Königsplatz', '101'),
        store('s2', 'München Hbf', '202'),
        store('s3', 'Nürnberg Hbf', '303'),
        store('s4', 'Geschlossen', '404', true),
      ]),
      holidayOpening: vi.fn().mockImplementation(async (id: string) => opening(id, open.has(id))),
      setHolidayOpening: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('lists the open stores by name with their current state and leaves out archived ones', async () => {
    await create();

    expect(names()).toEqual(['Augsburg Königsplatz', 'München Hbf', 'Nürnberg Hbf']);
    expect(stateOf('s1')).toBe('An Feiertagen geschlossen');
    expect(stateOf('s2')).toBe('An Feiertagen geöffnet');
    expect(api['holidayOpening']).not.toHaveBeenCalledWith('s4');
    expect(q('[data-testid="state-loading"]')).toBeNull();
  });

  it('reads the states at most five at a time', async () => {
    const many = Array.from({ length: 23 }, (_, i) => store(`x${i}`, `Filiale ${i}`, String(i)));
    api['stores'].mockResolvedValue(many);
    let running = 0;
    let peak = 0;
    api['holidayOpening'].mockImplementation(async (id: string) => {
      running++;
      peak = Math.max(peak, running);
      await new Promise((r) => setTimeout(r, 2));
      running--;
      return opening(id, false);
    });
    await create();
    for (let i = 0; i < 20; i++) {
      await settle();
    }

    expect(api['holidayOpening']).toHaveBeenCalledTimes(23);
    expect(peak).toBeLessThanOrEqual(STATE_CONCURRENCY);
    expect(peak).toBeGreaterThan(1);
  });

  it('filters by name or number, ignoring case and accents', async () => {
    await create();

    await search('nurnberg');
    expect(names()).toEqual(['Nürnberg Hbf']);
    await search('101');
    expect(names()).toEqual(['Augsburg Königsplatz']);
    await search('gibtesnicht');
    expect(q('[data-testid="no-stores"]')).not.toBeNull();
  });

  it('sets several stores at once with one call and updates their state', async () => {
    await create();
    await tick('s1');
    await tick('s3');
    expect(q('[data-testid="selected-count"]')!.textContent).toContain('2 ausgewählt');
    await click('[data-action="set-open"]');

    expect(api['setHolidayOpening']).toHaveBeenCalledTimes(1);
    expect(api['setHolidayOpening']).toHaveBeenCalledWith(['s1', 's3'], true);
    expect(stateOf('s1')).toBe('An Feiertagen geöffnet');
    expect(stateOf('s3')).toBe('An Feiertagen geöffnet');
    expect(q('[data-testid="status"]')!.textContent).toContain('2 Filialen sind jetzt an Feiertagen geöffnet');
    expect(q('[data-testid="selected-count"]')!.textContent).toContain('0 ausgewählt');
  });

  it('can mark stores as closed on holidays', async () => {
    await create();
    await tick('s2');
    await click('[data-action="set-closed"]');

    expect(api['setHolidayOpening']).toHaveBeenCalledWith(['s2'], false);
    expect(stateOf('s2')).toBe('An Feiertagen geschlossen');
    expect(q('[data-testid="status"]')!.textContent).toContain('1 Filiale ist jetzt');
  });

  it('selects all shown stores, only those that match the search', async () => {
    await create();
    await search('hbf');
    await click('[data-action="select-shown"]');
    await search('');

    expect(check('s1').checked).toBe(false);
    expect(check('s2').checked).toBe(true);
    expect(check('s3').checked).toBe(true);
    expect(q('[data-testid="selected-count"]')!.textContent).toContain('2 ausgewählt');
    await click('[data-action="clear"]');
    expect(q('[data-testid="selected-count"]')!.textContent).toContain('0 ausgewählt');
  });

  it('keeps the bulk buttons off until a store is selected', async () => {
    await create();

    expect(q<HTMLButtonElement>('[data-action="set-open"]')!.disabled).toBe(true);
    expect(q<HTMLButtonElement>('[data-action="set-closed"]')!.disabled).toBe(true);
    await tick('s1');
    expect(q<HTMLButtonElement>('[data-action="set-open"]')!.disabled).toBe(false);
  });

  it('does not let stores outside the reach be selected', async () => {
    api['holidayOpening'].mockImplementation(async (id: string) => {
      if (id === 's3') {
        throw new HttpErrorResponse({ status: 403 });
      }
      return opening(id, false);
    });
    await create();

    expect(check('s3').disabled).toBe(true);
    expect(stateOf('s3')).toContain('Nicht änderbar');
    await click('[data-action="select-shown"]');
    await click('[data-action="set-open"]');
    expect(api['setHolidayOpening']).toHaveBeenCalledWith(['s1', 's2'], true);
  });

  it('says nothing was changed when the service refuses (all or none)', async () => {
    api['setHolidayOpening'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();
    await tick('s1');
    await click('[data-action="set-open"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('Es wurde keine Filiale geändert');
    expect(stateOf('s1')).toBe('An Feiertagen geschlossen');
    expect(check('s1').checked).toBe(true);
  });

  it('refuses more than 500 stores before asking the service', async () => {
    const many = Array.from({ length: 501 }, (_, i) => store(`x${i}`, `Filiale ${i}`, String(i)));
    api['stores'].mockResolvedValue(many);
    await create();
    for (let i = 0; i < 40; i++) {
      await settle();
    }
    await click('[data-action="select-shown"]');
    await click('[data-action="set-open"]');

    expect(api['setHolidayOpening']).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('höchstens 500');
  }, 20_000);

  it('says so when the stores cannot be loaded', async () => {
    api['stores'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('Laden hat nicht geklappt');
    expect(q('[data-testid="search"]')).toBeNull();
  });
});
