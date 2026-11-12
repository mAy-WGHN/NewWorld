import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { LogbookInbox } from './logbook-inbox';
import { LogbookMine } from './logbook-mine';
import { LogbookMissing } from './logbook-missing';
import { LogbookPage } from './logbook.page';
import { LogbookApi } from './logbook.api';
import { LogbookTemplate, MissingStore, ReviewItem, StoreWeek, currentWeek, shiftWeek } from './logbook-models';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 4; i++) {
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r));
  }
}

const item = (patch: Partial<ReviewItem>): ReviewItem => ({
  id: 'r',
  storeId: 'st',
  storeNumber: '001',
  storeName: 'Augsburg',
  week: '2026-10-19',
  state: 'new',
  submittedAt: new Date(2026, 9, 23, 14, 0).toISOString(),
  revisedAt: null,
  followedUpAt: null,
  revisedSinceMyReply: false,
  commentCount: 0,
  escalated: false,
  ...patch,
});

describe('LogbookInbox', () => {
  let api: { list: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<LogbookInbox>;
  let root: HTMLElement;

  async function create(items: ReviewItem[]): Promise<void> {
    api.list.mockResolvedValue(items);
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: LogbookApi, useValue: api }] });
    fixture = TestBed.createComponent(LogbookInbox);
    root = fixture.nativeElement as HTMLElement;
    await settle(fixture);
  }

  const rows = (): string[] => [...root.querySelectorAll('[data-testid="inbox-row"]')].map((e) => e.getAttribute('data-id')!);

  beforeEach(() => {
    api = { list: vi.fn() };
  });

  it('asks for the current week and shows what needs a look first: followed up, new, in progress, done', async () => {
    await create([
      item({ id: 'done', state: 'done', storeNumber: '001' }),
      item({ id: 'progress', state: 'inProgress', storeNumber: '002' }),
      item({ id: 'new2', state: 'new', storeNumber: '004' }),
      item({ id: 'new1', state: 'new', storeNumber: '003' }),
      item({ id: 'followed', state: 'followedUp', storeNumber: '009' }),
    ]);

    expect(api.list).toHaveBeenCalledWith({ week: currentWeek(), open: undefined, state: undefined });
    expect(rows()).toEqual(['followed', 'new1', 'new2', 'progress', 'done']);
  });

  it('marks what changed since the leader replied, what was escalated and how many comments there are, and links to the report', async () => {
    await create([item({ id: 'a', revisedSinceMyReply: true, escalated: true, commentCount: 2 }), item({ id: 'b', commentCount: 1 })]);

    const a = root.querySelector('[data-id="a"]')!;
    expect(a.querySelector('[data-testid="revised"]')).not.toBeNull();
    expect(a.querySelector('[data-testid="escalated"]')).not.toBeNull();
    expect(a.textContent).toContain('2 Kommentare');
    expect(a.classList.contains('attention')).toBe(true);
    expect(a.querySelector('[data-action="open"]')!.getAttribute('href')).toBe('/logbook/reports/a');
    const b = root.querySelector('[data-id="b"]')!;
    expect(b.textContent).toContain('1 Kommentar');
    expect(b.querySelector('[data-testid="revised"]')).toBeNull();
    expect(b.classList.contains('attention')).toBe(false);
  });

  it('filters by store name or number without asking the service again', async () => {
    await create([item({ id: 'a', storeNumber: '001', storeName: 'Augsburg' }), item({ id: 'b', storeNumber: '002', storeName: 'München' })]);
    const field = root.querySelector<HTMLInputElement>('[data-testid="store-filter"]')!;

    field.value = 'münch';
    field.dispatchEvent(new Event('input'));
    await settle(fixture);
    expect(rows()).toEqual(['b']);

    field.value = '001';
    field.dispatchEvent(new Event('input'));
    await settle(fixture);
    expect(rows()).toEqual(['a']);
    expect(api.list).toHaveBeenCalledTimes(1);
  });

  it('goes a week back and asks for that week', async () => {
    await create([]);

    root.querySelector<HTMLButtonElement>('[data-action="previous-week"]')!.click();
    await settle(fixture);

    expect(api.list).toHaveBeenLastCalledWith({ week: shiftWeek(currentWeek(), -1), open: undefined, state: undefined });
  });

  it('shows all open reports of all weeks without a week when asked, and then needs no week to choose', async () => {
    await create([]);

    root.querySelector<HTMLInputElement>('[data-testid="open-only"] input')!.click();
    await settle(fixture);

    expect(api.list).toHaveBeenLastCalledWith({ week: undefined, open: true, state: undefined });
    expect(root.querySelector('app-logbook-week-nav')).toBeNull();
  });

  it('asks for one state if one is chosen', async () => {
    await create([]);
    const select = root.querySelector<HTMLSelectElement>('[data-testid="state-filter"]')!;

    select.value = 'followedUp';
    select.dispatchEvent(new Event('change'));
    await settle(fixture);

    expect(api.list).toHaveBeenLastCalledWith({ week: currentWeek(), open: undefined, state: 'followedUp' });
  });

  it('says so if there are no reports, and shows the error of the service otherwise', async () => {
    await create([]);
    expect(root.querySelector('[data-testid="empty"]')!.textContent).toContain('in dieser Woche');

    api.list.mockRejectedValue(new HttpErrorResponse({ status: 403, error: { success: false, message: 'Dafür fehlt dir das Recht.', errors: [] } }));
    root.querySelector<HTMLButtonElement>('[data-action="previous-week"]')!.click();
    await settle(fixture);
    expect(root.querySelector('[data-testid="error"]')!.textContent).toContain('Recht');
  });
});

describe('LogbookMissing', () => {
  let api: Record<'missing' | 'remind', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<LogbookMissing>;
  let root: HTMLElement;

  const store = (patch: Partial<MissingStore>): MissingStore => ({
    storeId: 'st1', storeNumber: '001', storeName: 'Augsburg', remindersSent: 0, lastReminderAt: null, hasStoreManagers: true, reportStarted: false, ...patch,
  });

  async function create(stores: MissingStore[]): Promise<void> {
    api.missing.mockResolvedValue({ week: currentWeek(), stores });
    TestBed.configureTestingModule({ providers: [{ provide: LogbookApi, useValue: api }] });
    fixture = TestBed.createComponent(LogbookMissing);
    root = fixture.nativeElement as HTMLElement;
    await settle(fixture);
  }

  const row = (id: string): HTMLElement => root.querySelector<HTMLElement>(`[data-store="${id}"]`)!;

  beforeEach(() => {
    api = { missing: vi.fn(), remind: vi.fn().mockResolvedValue(undefined) };
  });

  it('lists the stores without a report with what is known about reminders', async () => {
    await create([
      store({ storeId: 'a' }),
      store({ storeId: 'b', remindersSent: 2, lastReminderAt: new Date(2026, 9, 23, 12, 0).toISOString(), reportStarted: true }),
      store({ storeId: 'c', hasStoreManagers: false }),
    ]);

    expect(row('a').textContent).toContain('Noch nicht erinnert');
    expect(row('b').textContent).toContain('2× erinnert, zuletzt 23.10.2026, 12:00 Uhr');
    expect(row('b').textContent).toContain('Ein Entwurf ist angefangen');
    expect(row('c').textContent).toContain('keine Filialleitung eingetragen');
    expect(row('c').querySelector<HTMLButtonElement>('[data-action="remind"]')!.disabled).toBe(true);
    expect(row('a').querySelector<HTMLButtonElement>('[data-action="remind"]')!.disabled).toBe(false);
  });

  it('reminds the store managers of the week shown, says so and loads the list again', async () => {
    await create([store({ storeId: 'a' })]);
    api.missing.mockResolvedValue({ week: currentWeek(), stores: [store({ storeId: 'a', remindersSent: 1, lastReminderAt: new Date(2026, 9, 23, 12, 0).toISOString() })] });

    row('a').querySelector<HTMLButtonElement>('[data-action="remind"]')!.click();
    await settle(fixture);

    expect(api.remind).toHaveBeenCalledWith('a', currentWeek());
    expect(root.querySelector('[data-testid="remind-status"]')!.textContent).toContain('Augsburg');
    expect(row('a').textContent).toContain('1× erinnert');
  });

  it('shows why a reminder was refused', async () => {
    api.remind.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { success: false, message: 'Es wurde vor kurzem schon erinnert.', errors: [] } }));
    await create([store({ storeId: 'a' })]);

    row('a').querySelector<HTMLButtonElement>('[data-action="remind"]')!.click();
    await settle(fixture);

    expect(root.querySelector('[data-testid="remind-error"]')!.textContent).toContain('schon erinnert');
    expect(root.querySelector('[data-testid="remind-status"]')).toBeNull();
  });

  it('says that everything is in when nothing is missing', async () => {
    await create([]);

    expect(root.querySelector('[data-testid="none-missing"]')).not.toBeNull();
  });
});

describe('LogbookMine', () => {
  const template: LogbookTemplate = {
    version: 1,
    publishedAt: null,
    sections: [{ id: 's1', title: 'Stimmung', hint: null, kind: 'singleChoice', required: false, options: [{ id: 'o1', label: 'Gut' }] }],
  };
  const storeWeek = (id: string, number: string, version = 1): StoreWeek => ({ storeId: id, storeNumber: number, storeName: `Filiale ${number}`, templateVersion: version, canSubmit: true, report: null });

  let api: Record<'myReports' | 'template' | 'history', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<LogbookMine>;
  let root: HTMLElement;

  async function create(stores: StoreWeek[]): Promise<void> {
    api.myReports.mockResolvedValue({ week: currentWeek(), stores });
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: LogbookApi, useValue: api }] });
    fixture = TestBed.createComponent(LogbookMine);
    root = fixture.nativeElement as HTMLElement;
    await settle(fixture);
  }

  beforeEach(() => {
    api = {
      myReports: vi.fn(),
      template: vi.fn().mockResolvedValue(template),
      history: vi.fn().mockResolvedValue([]),
    };
  });

  it('shows one form per store of the person and loads each template version only once', async () => {
    await create([storeWeek('a', '001'), storeWeek('b', '002')]);

    expect(root.querySelectorAll('app-logbook-form').length).toBe(2);
    expect(api.template).toHaveBeenCalledTimes(1);
    expect(api.template).toHaveBeenCalledWith(1);
  });

  it('says so if no store is entered for the person', async () => {
    await create([]);

    expect(root.querySelector('[data-testid="no-stores"]')).not.toBeNull();
  });

  it('shows the last weeks of a store and opens the week that is clicked', async () => {
    api.history.mockResolvedValue([
      { id: 'r1', week: '2026-10-12', phase: 'submitted', state: 'done', submittedAt: null, revisedAt: null },
      { id: 'r0', week: '2026-10-05', phase: 'draft', state: 'new', submittedAt: null, revisedAt: null },
    ]);
    await create([storeWeek('a', '001')]);
    const rows = [...root.querySelectorAll('[data-testid="history-row"]')];
    expect(rows.map((r) => r.textContent)).toEqual([expect.stringContaining('KW 42'), expect.stringContaining('KW 41')]);
    expect(rows[0].textContent).toContain('Erledigt');
    expect(rows[1].textContent).toContain('Entwurf');

    rows[0].querySelector<HTMLButtonElement>('[data-action="open-week"]')!.click();
    await settle(fixture);

    expect(api.myReports).toHaveBeenLastCalledWith('2026-10-12');
  });

  it('loads the template of an older report with its own version', async () => {
    api.template.mockImplementation((version: number) => Promise.resolve({ ...template, version }));
    await create([storeWeek('a', '001', 1), storeWeek('b', '002', 4)]);

    expect(api.template.mock.calls.map((c) => c[0]).sort()).toEqual([1, 4]);
  });

  it('shows the error of the service if loading fails', async () => {
    api.myReports.mockRejectedValue(new HttpErrorResponse({ status: 403, error: { success: false, message: 'Dafür fehlt dir das Recht.', errors: [] } }));
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: LogbookApi, useValue: api }] });
    fixture = TestBed.createComponent(LogbookMine);
    root = fixture.nativeElement as HTMLElement;
    await settle(fixture);

    expect(root.querySelector('[data-testid="error"]')!.textContent).toContain('Recht');
  });
});

describe('LogbookPage', () => {
  let fixture: ComponentFixture<LogbookPage>;
  let root: HTMLElement;

  async function create(...privileges: string[]): Promise<void> {
    const api = {
      myReports: vi.fn().mockResolvedValue({ week: currentWeek(), stores: [] }),
      history: vi.fn().mockResolvedValue([]),
      template: vi.fn(),
      list: vi.fn().mockResolvedValue([]),
      missing: vi.fn().mockResolvedValue({ week: currentWeek(), stores: [] }),
    };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: LogbookApi, useValue: api }, { provide: AuthService, useValue: { privileges: signal(grant(...privileges)) } }],
    });
    fixture = TestBed.createComponent(LogbookPage);
    root = fixture.nativeElement as HTMLElement;
    await settle(fixture);
  }

  const tabs = (): string[] => [...root.querySelectorAll('[role="tab"]')].map((t) => t.textContent!.trim());

  it('shows a store manager the own report only', async () => {
    await create('logbook:write');

    expect(root.querySelector('h1')!.textContent).toContain('Berichtsheft');
    expect(tabs()).toEqual(['Mein Bericht', 'Auswertung']);
  });

  it('shows a leader the inbox and the missing reports, but not the own report', async () => {
    await create('logbook:review');

    expect(tabs()).toEqual(['Eingang', 'Fehlende Berichte', 'Auswertung']);
  });

  it('shows somebody who does both all three, with the own report first', async () => {
    await create('logbook:write', 'logbook:review');

    expect(tabs()).toEqual(['Mein Bericht', 'Eingang', 'Fehlende Berichte', 'Auswertung']);
  });
});
