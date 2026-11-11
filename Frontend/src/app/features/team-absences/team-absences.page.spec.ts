import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeGrant, PrivilegeSet } from '../../core/auth/privileges';
import {
  StoreUnavailability,
  UnavailabilityKind,
  UnavailabilityPeriod,
  cellsFor,
  entryErrorMessage,
  membersOf,
  membersWithPeople,
  monthDays,
  monthEnd,
  shiftMonth,
  validateEntry,
} from './team-absences-models';
import { TeamAbsencesApi } from './team-absences.api';
import { TeamAbsencesPage } from './team-absences.page';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const scoped = (key: string, storeIds: string[]): PrivilegeGrant => ({ key, unrestricted: false, storeIds, employeeIds: [] });

const stores = [
  { id: 's1', name: 'Augsburg Hbf' },
  { id: 's2', name: 'München Mitte' },
];

const period = (over: Partial<UnavailabilityPeriod> = {}): UnavailabilityPeriod => ({
  employeeId: 'e1', from: '2026-11-03', to: '2026-11-05', status: 'Unavailable', id: 'x1', kind: 'KRANK', description: 'Krank', origin: 'Absence', afterExit: false, ...over,
});

const row = (name: string, over: Partial<UnavailabilityPeriod> = {}): StoreUnavailability => ({ employeeName: name, period: period(over) });

const kind = (over: Partial<UnavailabilityKind> = {}): UnavailabilityKind => ({
  id: 'k1', code: 'SOND', description: 'Sonderurlaub', paidTime: true, scheduleSymbol: null, workCode: null, isActive: true, isSystem: false, ...over,
});

const problem = (title: string, status = 409) => new HttpErrorResponse({ status, error: { title } });

describe('TeamAbsencesPage', () => {
  let api: Record<'stores' | 'store' | 'people' | 'mine' | 'kinds' | 'add' | 'remove', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant('absence:read-availability'));
  let fixture: ComponentFixture<TeamAbsencesPage>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: TeamAbsencesApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(TeamAbsencesPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const qa = (selector: string): HTMLElement[] => [...root.querySelectorAll<HTMLElement>(selector)];

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  async function choose(selector: string, value: string): Promise<void> {
    const select = q<HTMLSelectElement>(selector)!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const input = q<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'], now: new Date(2026, 10, 15, 12) });
    privileges.set(grant('absence:read-availability', 'absence:read-own'));
    api = {
      stores: vi.fn().mockResolvedValue(stores),
      people: vi.fn().mockResolvedValue([]),
      store: vi.fn().mockResolvedValue([row('Erika Mustermann'), row('Erika Mustermann', { from: '2026-11-10', to: '2026-11-10', kind: 'URLAUB' })]),
      mine: vi.fn().mockResolvedValue([period({ from: '2026-10-01', to: '2026-10-02', kind: 'URLAUB' }), period()]),
      kinds: vi.fn().mockResolvedValue([kind(), kind({ id: 'k2', code: 'KRANK', description: 'Krank', isSystem: true })]),
      add: vi.fn().mockResolvedValue({}),
      remove: vi.fn().mockResolvedValue(undefined),
    };
  });

  afterEach(() => vi.useRealTimers());

  it('loads the first store for the current month and shows the colours with letters', async () => {
    await create();

    expect(api['store']).toHaveBeenCalledWith('s1', '2026-11-01', '2026-11-30');
    expect(q('[data-testid="month"]')!.textContent).toContain('November 2026');
    expect(qa('[data-testid="person-row"]').length).toBe(1);
    const cells = qa('[data-testid="cell"]');
    expect(cells.filter((c) => c.dataset['kind'] === 'sick').length).toBe(3);
    expect(cells.filter((c) => c.dataset['kind'] === 'vacation').length).toBe(1);
    expect(cells[0].getAttribute('aria-label')).toBe('Krank');
    expect(qa('[data-testid="legend"] li').length).toBe(5);
  });

  it('shows a row for every active person of the store, also those without an entry', async () => {
    api['people'].mockResolvedValue([{ employeeId: 'e1', name: 'Erika Mustermann' }, { employeeId: 'e9', name: 'Anna Ohne Eintrag' }]);
    await create();

    expect(api['people']).toHaveBeenCalledWith('s1');
    expect(qa('[data-testid="person-row"]').map((r) => r.textContent)).toEqual([expect.stringContaining('Anna Ohne Eintrag'), expect.stringContaining('Erika Mustermann')]);
  });

  it('still shows the people with an entry when the list of the people cannot be loaded', async () => {
    api['people'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(qa('[data-testid="person-row"]').length).toBe(1);
    expect(q('[data-testid="load-error"]')).toBeNull();
  });

  it('marks weekends and today', async () => {
    await create();

    const days = qa('[data-testid="day"]');
    expect(days.length).toBe(30);
    expect(days.filter((d) => d.dataset['today'] === 'true').map((d) => d.textContent!.trim())).toEqual(['So15']);
    // 1 Nov 2026 is a Sunday: 1, 7, 8, 14, 15, 21, 22, 28, 29
    expect(days.filter((d) => d.dataset['weekend'] === 'true').length).toBe(9);
  });

  it('shows what the service sends: only "nicht verfügbar" where no kind is given, never a guessed reason', async () => {
    api['store'].mockResolvedValue([row('Ben Beispiel', { kind: null, description: null, id: null, origin: null })]);
    await create();

    const cells = qa('[data-testid="cell"]');
    expect(cells.length).toBe(3);
    expect(cells.every((c) => c.dataset['kind'] === 'unavailable' && c.getAttribute('aria-label') === 'Nicht verfügbar')).toBe(true);
  });

  it('shows another kind with its description when the viewer may see it', async () => {
    api['store'].mockResolvedValue([row('Ben Beispiel', { kind: 'SOND', description: 'Sonderurlaub' })]);
    await create();

    expect(qa('[data-testid="cell"]')[0].dataset['kind']).toBe('other');
    expect(qa('[data-testid="cell"]')[0].getAttribute('aria-label')).toBe('Sonderurlaub');
  });

  it('goes to the next and previous month and asks again', async () => {
    await create();

    await click('[data-action="next"]');
    expect(api['store']).toHaveBeenLastCalledWith('s1', '2026-12-01', '2026-12-31');
    await click('[data-action="previous"]');
    await click('[data-action="previous"]');
    expect(api['store']).toHaveBeenLastCalledWith('s1', '2026-10-01', '2026-10-31');
    await click('[data-action="today"]');
    expect(api['store']).toHaveBeenLastCalledWith('s1', '2026-11-01', '2026-11-30');
  });

  it('asks again for the chosen store', async () => {
    await create();

    await choose('[data-testid="store"]', 's2');

    expect(api['store']).toHaveBeenLastCalledWith('s2', '2026-11-01', '2026-11-30');
  });

  it('offers only the stores of the person if the privilege is restricted', async () => {
    privileges.set(new PrivilegeSet([scoped('absence:read-availability', ['s2'])]));
    await create();

    expect(qa('[data-testid="store"] option').map((o) => o.textContent!.trim())).toEqual(['München Mitte']);
    expect(api['store']).toHaveBeenCalledWith('s2', '2026-11-01', '2026-11-30');
  });

  it('joins the stores of both privileges', async () => {
    privileges.set(new PrivilegeSet([scoped('absence:read-availability', ['s1']), scoped('absence:read-reason', ['s2'])]));
    await create();

    expect(qa('[data-testid="store"] option').length).toBe(2);
  });

  it('says so and asks for nothing if no store is allowed', async () => {
    privileges.set(new PrivilegeSet([scoped('absence:read-availability', ['unknown'])]));
    await create();

    expect(q('[data-testid="no-stores"]')).not.toBeNull();
    expect(api['store']).not.toHaveBeenCalled();
  });

  it('says so when nobody is absent and when the list cannot be loaded', async () => {
    api['store'].mockResolvedValue([]);
    await create();
    expect(q('[data-testid="empty"]')).not.toBeNull();

    api['store'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await click('[data-action="next"]');
    expect(q('[data-testid="load-error"]')!.textContent).toContain('konnten nicht geladen werden');
    expect(q('[data-testid="empty"]')).toBeNull();
  });

  it('shows the own timeline as the second view', async () => {
    await create();

    await click('[data-testid="view-mine"]');

    expect(api['mine']).toHaveBeenCalledTimes(1);
    const rows = qa('[data-testid="mine-row"]');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Krank');
    expect(rows[0].textContent).toContain('03.11.2026 – 05.11.2026');
  });

  it('says so if the own timeline cannot be loaded', async () => {
    api['mine'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    await click('[data-testid="view-mine"]');

    expect(q('[data-testid="mine-error"]')).not.toBeNull();
  });

  it('offers the own view only with the privilege to read the own absences', async () => {
    privileges.set(grant('absence:read-availability'));
    await create();

    expect(q('[data-testid="view-mine"]')).toBeNull();
  });

  describe('HR entries', () => {
    beforeEach(() => {
      privileges.set(grant('absence:read-reason', 'unavailability:create', 'unavailability:delete'));
      api['store'].mockResolvedValue([
        row('Erika Mustermann', { id: 'm1', kind: 'SOND', description: 'Sonderurlaub', origin: 'Manual', afterExit: true }),
        row('Erika Mustermann', { id: null, origin: 'Absence' }),
      ]);
    });

    it('hides the controls without the privileges', async () => {
      privileges.set(grant('absence:read-reason'));
      await create();

      expect(q('[data-testid="manage"]')).toBeNull();
      expect(api['kinds']).not.toHaveBeenCalled();
    });

    it('shows only the delete part to someone who may only delete, and only the form to someone who may only create', async () => {
      privileges.set(grant('absence:read-reason', 'unavailability:delete'));
      await create();
      await choose('[data-testid="person"]', 'e1');
      expect(q('[data-testid="add-form"]')).toBeNull();
      expect(q('[data-testid="entry"]')).not.toBeNull();
      fixture.destroy();
      TestBed.resetTestingModule();

      privileges.set(grant('absence:read-reason', 'unavailability:create'));
      await create();
      await choose('[data-testid="person"]', 'e1');
      expect(q('[data-testid="add-form"]')).not.toBeNull();
      expect(q('[data-testid="entry"]')).toBeNull();
    });

    it('offers only kinds HR may enter (not the system kinds)', async () => {
      await create();

      expect(qa('[data-testid="kind"] option').map((o) => o.textContent!.trim())).toEqual(['Bitte wählen', 'Sonderurlaub']);
    });

    it('adds an entry for the chosen person and loads the grid again', async () => {
      await create();
      await choose('[data-testid="person"]', 'e1');
      await choose('[data-testid="kind"]', 'k1');
      await type('[data-testid="from"]', '2026-11-20');
      await type('[data-testid="to"]', '2026-11-22');

      await click('[data-action="add"]');

      expect(api['add']).toHaveBeenCalledWith('e1', { kindId: 'k1', from: '2026-11-20', to: '2026-11-22' });
      expect(q('[data-testid="status"]')!.textContent).toContain('Eintrag gespeichert');
      expect(api['store']).toHaveBeenCalledTimes(2);
    });

    it('does not call the service for a missing person, kind, or an end before the start', async () => {
      await create();

      await click('[data-action="add"]');
      expect(q('[role="alert"]')!.textContent).toContain('Person');

      await choose('[data-testid="person"]', 'e1');
      await choose('[data-testid="kind"]', 'k1');
      await type('[data-testid="from"]', '2026-11-22');
      await type('[data-testid="to"]', '2026-11-20');
      await click('[data-action="add"]');

      expect(q('[role="alert"]')!.textContent).toContain('Das Ende liegt vor dem Beginn.');
      expect(api['add']).not.toHaveBeenCalled();
    });

    it('translates the answer of the service', async () => {
      api['add'].mockRejectedValue(problem('Overlap'));
      await create();
      await choose('[data-testid="person"]', 'e1');
      await choose('[data-testid="kind"]', 'k1');
      await type('[data-testid="from"]', '2026-11-20');
      await type('[data-testid="to"]', '2026-11-22');

      await click('[data-action="add"]');

      expect(q('[role="alert"]')!.textContent).toContain('überschneidet sich');
    });

    it('lists only the entries entered by hand and marks one that reaches past the exit', async () => {
      await create();

      await choose('[data-testid="person"]', 'e1');

      expect(qa('[data-testid="entry"]').length).toBe(1);
      expect(q('[data-testid="entry"]')!.textContent).toContain('Sonderurlaub');
      expect(q('[data-testid="after-exit"]')).not.toBeNull();
    });

    it('asks before it deletes and then deletes for the right person', async () => {
      await create();
      await choose('[data-testid="person"]', 'e1');

      await click('[data-action="ask-delete"]');
      expect(api['remove']).not.toHaveBeenCalled();
      await click('[data-action="delete"]');

      expect(api['remove']).toHaveBeenCalledWith('e1', 'm1');
      expect(q('[data-testid="status"]')!.textContent).toContain('Eintrag gelöscht');
    });

    it('can cancel the deletion, and shows why the service refused one', async () => {
      api['remove'].mockRejectedValue(problem('SystemEntry'));
      await create();
      await choose('[data-testid="person"]', 'e1');

      await click('[data-action="ask-delete"]');
      await click('[data-action="cancel-delete"]');
      expect(q('[data-testid="delete-panel"]')).toBeNull();

      await click('[data-action="ask-delete"]');
      await click('[data-action="delete"]');
      expect(q('[role="alert"]')!.textContent).toContain('folgt einer Krankmeldung');
    });
  });
});

describe('team absences models', () => {
  it('shifts months over the year change', () => {
    expect(shiftMonth({ year: 2026, month: 12 }, 1)).toEqual({ year: 2027, month: 1 });
    expect(shiftMonth({ year: 2026, month: 1 }, -1)).toEqual({ year: 2025, month: 12 });
    expect(shiftMonth({ year: 2026, month: 3 }, -15)).toEqual({ year: 2024, month: 12 });
  });

  it('knows the end of February in leap and other years', () => {
    expect(monthEnd({ year: 2028, month: 2 })).toBe('2028-02-29');
    expect(monthEnd({ year: 2026, month: 2 })).toBe('2026-02-28');
    expect(monthEnd({ year: 1900, month: 2 })).toBe('1900-02-28');
    expect(monthEnd({ year: 2000, month: 2 })).toBe('2000-02-29');
  });

  it('has the right days and weekdays around the daylight saving change', () => {
    const march = monthDays({ year: 2026, month: 3 });
    expect(march.length).toBe(31);
    // 29 Mar 2026: clocks go forward, still just a Sunday.
    expect(march[28]).toMatchObject({ iso: '2026-03-29', weekday: 'So', weekend: true });
    expect(monthDays({ year: 2026, month: 10 })[24]).toMatchObject({ iso: '2026-10-25', weekday: 'So' });
  });

  it('fills a period over the month change and an open end up to the last day', () => {
    const days = monthDays({ year: 2026, month: 2 });
    const cells = cellsFor([period({ from: '2026-01-30', to: '2026-02-02' }), period({ from: '2026-02-27', to: null, kind: 'URLAUB' })], days);
    expect(cells.slice(0, 3).map((c) => c?.kind ?? null)).toEqual(['sick', 'sick', null]);
    expect(cells.slice(26).map((c) => c?.kind)).toEqual(['vacation', 'vacation']);
  });

  it('shows a day with sickness in vacation as KU first, then sickness, then vacation, then others', () => {
    const days = monthDays({ year: 2028, month: 2 });
    const all = [
      period({ from: '2028-02-29', to: '2028-02-29', kind: 'URLAUB' }),
      period({ from: '2028-02-29', to: '2028-02-29', kind: 'SOND' }),
      period({ from: '2028-02-29', to: '2028-02-29', kind: 'KRANK I URLAUB' }),
      period({ from: '2028-02-29', to: '2028-02-29', kind: 'KRANK' }),
    ];
    expect(cellsFor(all, days)[28]!.kind).toBe('ku');
    expect(cellsFor(all.slice(0, 2), days)[28]!.kind).toBe('vacation');
  });

  it('lists the people of the store and those with a period, each once, sorted by name', () => {
    const members = membersWithPeople(
      [{ employeeId: 'z', name: 'Zoe' }, { employeeId: 'a', name: 'Anna' }],
      [row('Anna', { employeeId: 'a' }), row('Ben (ausgetreten)', { employeeId: 'b' })],
    );
    expect(members.map((m) => m.name)).toEqual(['Anna', 'Ben (ausgetreten)', 'Zoe']);
    expect(membersWithPeople([], [row('Anna', { employeeId: 'a' })]).map((m) => m.name)).toEqual(['Anna']);
  });

  it('lists each person once, sorted by name', () => {
    const members = membersOf([row('Zoe', { employeeId: 'z' }), row('Anna', { employeeId: 'a' }), row('Anna', { employeeId: 'a' })]);
    expect(members.map((m) => m.name)).toEqual(['Anna', 'Zoe']);
  });

  it('validates an entry', () => {
    expect(validateEntry({ kindId: '', from: '2026-11-01', to: '2026-11-02' })).toContain('Art');
    expect(validateEntry({ kindId: 'k', from: '', to: '2026-11-02' })).toContain('Beginn');
    expect(validateEntry({ kindId: 'k', from: '2026-11-01', to: '' })).toContain('Ende');
    expect(validateEntry({ kindId: 'k', from: '2026-11-02', to: '2026-11-01' })).toContain('vor dem Beginn');
    expect(validateEntry({ kindId: 'k', from: '2028-02-29', to: '2028-02-29' })).toBeNull();
  });

  it('names the errors of the service in plain words and falls back to a general text', () => {
    expect(entryErrorMessage('AfterExit')).toContain('Austritt');
    expect(entryErrorMessage('InvalidPeriod')).toContain('Zeitraum');
    expect(entryErrorMessage('KindNotEnterable')).toContain('Abwesenheiten');
    expect(entryErrorMessage('Whatever')).toContain('nicht geklappt');
    expect(entryErrorMessage(undefined)).toContain('nicht geklappt');
  });
});
