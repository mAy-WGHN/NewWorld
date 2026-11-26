import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { CashRegisterApi } from './cash-register.api';
import { CashRegister, CashRegisterList, cashRegisterError, filterRegisters, formatDateTime, isValidTime, kindLabel } from './cash-register-models';
import { CashRegistersPage } from './cash-registers.page';

const register = (patch: Partial<CashRegister>): CashRegister => ({
  id: 'r1',
  storeId: 's10',
  storeNumber: '0010',
  storeName: 'Augsburg Oberhausen',
  name: 'WGHN Kasse 1',
  sealed: false,
  sealedAt: null,
  lastRecordedAt: '2026-11-25T15:52:00Z',
  recordedBy: 'AvS',
  recordedByName: 'Anna von Schmidt',
  ...patch,
});

const first = register({});
const second = register({ id: 'r2', storeId: 's12', storeNumber: '0012', storeName: 'Augsburg HBF BaBu', name: 'Kasse 1', sealed: true, sealedAt: '2026-11-26T11:50:00Z' });

const list = (items: CashRegister[]): CashRegisterList => ({
  summary: { stores: 38, registers: items.length, sealed: items.filter((i) => i.sealed).length, notSealed: items.filter((i) => !i.sealed).length },
  stores: [
    { id: 's10', number: '0010', name: 'Augsburg Oberhausen' },
    { id: 's12', number: '0012', name: 'Augsburg HBF BaBu' },
  ],
  items,
});

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

describe('cash register models', () => {
  it('filters by status, store and search text', () => {
    const items = [first, second];
    expect(filterRegisters(items, 'all', '', '')).toHaveLength(2);
    expect(filterRegisters(items, 'sealed', '', '')).toEqual([second]);
    expect(filterRegisters(items, 'notSealed', '', '')).toEqual([first]);
    expect(filterRegisters(items, 'all', 's12', '')).toEqual([second]);
    expect(filterRegisters(items, 'all', '', 'oberhausen')).toEqual([first]);
    expect(filterRegisters(items, 'all', '', '0012')).toEqual([second]);
    expect(filterRegisters(items, 'sealed', 's10', '')).toEqual([]);
  });

  it('names the steps of the history in German and leaves unknown ones as they are', () => {
    expect(kindLabel('recorded')).toBe('Erfasst');
    expect(kindLabel('sealed')).toBe('Siegel erneuert');
    expect(kindLabel('deleted')).toBe('Gelöscht');
    expect(kindLabel('neu')).toBe('neu');
  });

  it('knows a valid time of day', () => {
    for (const ok of ['00:00', '08:05', '18:00', '23:59']) {
      expect(isValidTime(ok)).toBe(true);
    }
    for (const bad of ['', '8:05', '24:00', '18:60', '18', '18:00 Uhr']) {
      expect(isValidTime(bad)).toBe(false);
    }
  });

  it('formats dates and leaves a broken value as it is', () => {
    expect(formatDateTime('kaputt')).toBe('kaputt');
    expect(formatDateTime('2026-11-26T12:43:00Z')).toMatch(/^\d{2}\.\d{2}\.\d{2}, \d{2}:\d{2}$/);
  });

  it('takes the validation messages of the service, else its message, else a plain text', () => {
    const validation = new HttpErrorResponse({ status: 422, error: { success: false, message: 'Validierung fehlgeschlagen.', errors: [{ field: 'name', message: 'Dieser Wert darf nicht leer sein.' }] } });
    const conflict = new HttpErrorResponse({ status: 409, error: { success: false, message: 'In dieser Filiale gibt es schon eine Kasse mit diesem Namen.', errors: [] } });
    expect(cashRegisterError(validation)).toBe('Dieser Wert darf nicht leer sein.');
    expect(cashRegisterError(conflict)).toContain('schon eine Kasse');
    expect(cashRegisterError(new Error('boom'))).toContain('Fehler');
  });
});

describe('CashRegisterApi', () => {
  let api: CashRegisterApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(CashRegisterApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('unwraps the envelope of the list', async () => {
    const done = api.list();
    http.expectOne('/svc/portal/api/cash-registers').flush({ success: true, data: list([first]) });
    expect((await done).items).toEqual([first]);
  });

  it('sends create, edit, seal and delete to the right places', async () => {
    const created = api.create('s10', 'Kasse 2');
    const create = http.expectOne('/svc/portal/api/cash-registers');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ storeId: 's10', name: 'Kasse 2' });
    create.flush({ success: true, data: first });

    const edited = api.update('r1', 's12', 'Kasse 3');
    const edit = http.expectOne('/svc/portal/api/cash-registers/r1');
    expect(edit.request.method).toBe('PUT');
    expect(edit.request.body).toEqual({ storeId: 's12', name: 'Kasse 3' });
    edit.flush({ success: true, data: first });

    const sealed = api.seal('r1');
    const seal = http.expectOne('/svc/portal/api/cash-registers/r1/seal');
    expect(seal.request.method).toBe('POST');
    seal.flush({ success: true, data: second });

    const deleted = api.delete('r1');
    const del = http.expectOne('/svc/portal/api/cash-registers/r1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });

    await Promise.all([created, edited, sealed, deleted]);
  });

  it('asks for the history of one store or of all', async () => {
    const everything = api.history();
    const withoutStore = http.expectOne('/svc/portal/api/cash-registers/history');
    expect(withoutStore.request.params.keys()).toEqual([]);
    withoutStore.flush({ success: true, data: [] });
    expect(await everything).toEqual([]);

    const oneStore = api.history('s10');
    const withStore = http.expectOne((r) => r.url === '/svc/portal/api/cash-registers/history');
    expect(withStore.request.params.get('storeId')).toBe('s10');
    withStore.flush({ success: true, data: [] });
    expect(await oneStore).toEqual([]);
  });
});

describe('CashRegistersPage', () => {
  let api: Record<'list' | 'create' | 'update' | 'seal' | 'delete' | 'history' | 'reminderSettings' | 'saveReminderSettings', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<CashRegistersPage>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(privileges = grant('cashregister:read', 'cashregister:record', 'cashregister:delete')): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: CashRegisterApi, useValue: api },
        { provide: AuthService, useValue: { privileges: signal(privileges) } },
      ],
    });
    fixture = TestBed.createComponent(CashRegistersPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const all = (selector: string) => [...root.querySelectorAll<HTMLElement>(selector)];
  const click = async (element: HTMLElement | null) => {
    element!.click();
    await settle();
  };
  const type = async (selector: string, value: string, event = 'input') => {
    const element = q<HTMLInputElement | HTMLSelectElement>(selector)!;
    element.value = value;
    element.dispatchEvent(new Event(event));
    await settle();
  };

  beforeEach(() => {
    api = {
      list: vi.fn().mockResolvedValue(list([first, second])),
      create: vi.fn().mockResolvedValue(first),
      update: vi.fn().mockResolvedValue(first),
      seal: vi.fn().mockResolvedValue(second),
      delete: vi.fn().mockResolvedValue(undefined),
      reminderSettings: vi.fn().mockResolvedValue({ enabled: true, time: '18:00' }),
      saveReminderSettings: vi.fn(),
      history: vi.fn().mockResolvedValue([
        { id: 'e1', storeId: 's10', storeNumber: '0010', storeName: 'Augsburg Oberhausen', registerId: 'r1', registerName: 'WGHN Kasse 1', kind: 'recorded', at: '2026-11-25T15:52:00Z', by: 'AvS', byName: 'Anna von Schmidt' },
      ]),
    };
  });

  it('shows the four numbers and the registers with status, time and initials', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Kassenregister');
    expect(q('[data-testid="stores-total"]')!.textContent).toContain('38');
    expect(q('[data-testid="registers-total"]')!.textContent).toContain('2');
    expect(q('[data-testid="sealed-total"]')!.textContent).toContain('1');
    expect(q('[data-testid="not-sealed-total"]')!.textContent).toContain('1');
    const rows = all('[data-testid="row"]');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('0010 Augsburg Oberhausen');
    expect(rows[0].textContent).toContain('Nicht versiegelt');
    expect(rows[0].textContent).toContain('AvS');
    expect(rows[1].textContent).toContain('Versiegelt');
  });

  it('filters by status, by store and by the search text', async () => {
    await create();

    await click(q('[data-testid="filter-sealed"]'));
    expect(all('[data-testid="row"]')).toHaveLength(1);
    expect(all('[data-testid="row"]')[0].textContent).toContain('Kasse 1');
    expect(q('[data-testid="filter-sealed"]')!.getAttribute('aria-pressed')).toBe('true');

    await click(q('[data-testid="filter-all"]'));
    await type('[data-testid="store-filter"]', 's10', 'change');
    expect(all('[data-testid="row"]')).toHaveLength(1);
    expect(all('[data-testid="row"]')[0].textContent).toContain('Oberhausen');

    await type('[data-testid="store-filter"]', '', 'change');
    await type('[data-testid="search"]', 'zzz');
    expect(q('[data-testid="empty"]')!.textContent).toContain('passt zu diesen Filtern');
  });

  it('records a register: the form asks for store and name and reloads the list', async () => {
    await create();

    await click(q('[data-testid="create-button"]'));
    // store and name are required fields: the browser stops an empty form before it reaches the service
    await click(q('[data-testid="form-save"]'));
    expect(api.create).not.toHaveBeenCalled();
    expect(q('[data-testid="form"]')).not.toBeNull();

    await type('[data-testid="form-store"]', 's12', 'change');
    await type('[data-testid="form-name"]', ' Kasse 2 ');
    await click(q('[data-testid="form-save"]'));

    expect(api.create).toHaveBeenCalledWith('s12', 'Kasse 2');
    expect(api.list).toHaveBeenCalledTimes(2);
    expect(q('[data-testid="form"]')).toBeNull();
    expect(q('[data-testid="status"]')!.textContent).toContain('erfasst');
  });

  it('edits a register with its own values and says that the seal falls away', async () => {
    await create();

    await click(all('[data-action="edit"]')[0]);
    expect((q('[data-testid="form-name"]') as HTMLInputElement).value).toBe('WGHN Kasse 1');
    expect(q('[data-testid="form"]')!.textContent).toContain('Siegel entfällt');
    await type('[data-testid="form-name"]', 'WGHN Kasse 9');
    await click(q('[data-testid="form-save"]'));

    expect(api.update).toHaveBeenCalledWith('r1', 's10', 'WGHN Kasse 9');
  });

  it('renews the seal', async () => {
    await create();

    await click(all('[data-action="seal"]')[0]);

    expect(api.seal).toHaveBeenCalledWith('r1');
    expect(q('[data-testid="status"]')!.textContent).toContain('Siegel');
  });

  it('asks before deleting: the first click only asks, the second deletes', async () => {
    await create();

    await click(all('[data-action="delete"]')[0]);
    expect(api.delete).not.toHaveBeenCalled();
    expect(all('[data-action="delete"]')[0].textContent).toContain('Wirklich löschen?');

    await click(all('[data-action="delete"]')[0]);
    expect(api.delete).toHaveBeenCalledWith('r1');
  });

  it('shows a failure of the service as a plain message and keeps the form', async () => {
    api.create.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { success: false, message: 'In dieser Filiale gibt es schon eine Kasse mit diesem Namen.', errors: [] } }));
    await create();

    await click(q('[data-testid="create-button"]'));
    await type('[data-testid="form-store"]', 's10', 'change');
    await type('[data-testid="form-name"]', 'WGHN Kasse 1');
    await click(q('[data-testid="form-save"]'));

    expect(q('[data-testid="error"]')!.textContent).toContain('schon eine Kasse');
    expect(q('[data-testid="form"]')).not.toBeNull();
  });

  it('opens the history of the chosen store and goes back to the list', async () => {
    await create();

    await type('[data-testid="store-filter"]', 's10', 'change');
    await click(q('[data-testid="history-button"]'));

    expect(api.history).toHaveBeenCalledWith('s10');
    expect(all('[data-testid="history-row"]')).toHaveLength(1);
    expect(all('[data-testid="history-row"]')[0].textContent).toContain('Erfasst');
    expect(q('[data-testid="table"]')).toBeNull();

    await click(q('[data-testid="history-button"]'));
    expect(q('[data-testid="table"]')).not.toBeNull();
  });

  it('offers only what the privileges allow', async () => {
    await create(grant('cashregister:read'));

    expect(q('[data-testid="create-button"]')).toBeNull();
    expect(all('[data-action]')).toHaveLength(0);
    expect(all('[data-testid="row"]')).toHaveLength(2);
  });

  it('shows the reminder settings only with the privilege and saves them', async () => {
    api.reminderSettings = vi.fn().mockResolvedValue({ enabled: true, time: '18:00' });
    api.saveReminderSettings = vi.fn().mockImplementation((s) => Promise.resolve(s));
    await create(grant('cashregister:read', 'cashregister:manage-reminders'));

    expect((q('[data-testid="reminder-time"]') as HTMLInputElement).value).toBe('18:00');
    await type('[data-testid="reminder-time"]', '07:45');
    await click(q('[data-testid="reminder-save"]'));

    expect(api.saveReminderSettings).toHaveBeenCalledWith({ enabled: true, time: '07:45' });
    expect(q('[data-testid="reminder-status"]')!.textContent).toContain('07:45');

    q('[data-testid="reminder-enabled"]')!.click();
    await settle();
    await click(q('[data-testid="reminder-save"]'));
    expect(api.saveReminderSettings).toHaveBeenLastCalledWith({ enabled: false, time: '07:45' });
    expect(q('[data-testid="reminder-status"]')!.textContent).toContain('ausgeschaltet');
  });

  it('does not offer the reminder settings without the privilege and refuses a wrong time', async () => {
    api.reminderSettings = vi.fn().mockResolvedValue({ enabled: true, time: '18:00' });
    api.saveReminderSettings = vi.fn();
    await create(grant('cashregister:read'));
    expect(q('[data-testid="reminder"]')).toBeNull();
    expect(api.reminderSettings).not.toHaveBeenCalled();
  });

  it('refuses a time that is not HH:mm before it reaches the service', async () => {
    api.reminderSettings = vi.fn().mockResolvedValue({ enabled: true, time: '18:00' });
    api.saveReminderSettings = vi.fn();
    await create(grant('cashregister:read', 'cashregister:manage-reminders'));

    await type('[data-testid="reminder-time"]', '');
    await click(q('[data-testid="reminder-save"]'));

    expect(api.saveReminderSettings).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('HH:mm');
  });

  it('tells when there is no register yet', async () => {
    api.list.mockResolvedValue(list([]));
    await create();

    expect(q('[data-testid="empty"]')!.textContent).toContain('Noch keine Kasse erfasst');
  });

  it('shows the error when the list cannot be loaded', async () => {
    api.list.mockRejectedValue(new HttpErrorResponse({ status: 403, error: { success: false, message: 'Keine Berechtigung.', errors: [] } }));
    await create();

    expect(q('[data-testid="error"]')!.textContent).toContain('Keine Berechtigung');
  });
});
