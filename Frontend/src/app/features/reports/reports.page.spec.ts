import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { Report } from './report-models';
import { ReportsApi } from './reports.api';
import { ReportsPage } from './reports.page';

const make = (patch: Partial<Report>): Report => ({
  id: 'r',
  personnelNumber: 4711,
  status: 'open',
  source: 'employee',
  category: 'bug',
  description: 'Etwas ist kaputt',
  severity: 'medium',
  locations: [{ id: 'l1', name: 'Augsburg', storeCode: '017', archived: false }],
  device: null,
  noticedAt: null,
  noticedHow: null,
  priorActivity: null,
  comment: null,
  archived: false,
  parentReportId: null,
  childReportIds: [],
  createdAt: '2026-11-04T09:30:00',
  updatedAt: '2026-11-04T09:30:00',
  ...patch,
});

const open1 = make({ id: 'o1', category: 'theft', severity: 'critical', description: 'Kasse stimmt nicht', locations: [{ id: 'l1', name: 'Augsburg', storeCode: '017', archived: false }, { id: 'l2', name: 'München', storeCode: '018', archived: false }] });
const open2 = make({ id: 'o2', category: 'virus', severity: 'low', comment: 'Wird geprüft' });
const done1 = make({ id: 'd1', status: 'done', category: 'suggestion', description: 'Mehr Licht' });
const archived1 = make({ id: 'a1', status: 'done', archived: true, description: 'Altes Thema' });

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const pageOf = (items: Report[], total = items.length) => ({ items, meta: { total, offset: 0, limit: 20 } });
const envelope = (message: string, status = 403) => new HttpErrorResponse({ status, error: { success: false, message, errors: [] } });

describe('ReportsPage', () => {
  let api: Record<'list' | 'listArchived' | 'setStatus' | 'saveComment' | 'archive' | 'restore', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<ReportsPage>;
  let root: HTMLElement;

  async function create(privileges = grant('report:read', 'report:update', 'report:delete')): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: ReportsApi, useValue: api },
        { provide: AuthService, useValue: { privileges: signal(privileges) } },
      ],
    });
    fixture = TestBed.createComponent(ReportsPage);
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
  const ids = (selector: string) => [...root.querySelectorAll(selector)].map((e) => e.getAttribute('data-id'));

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  beforeEach(() => {
    api = {
      list: vi.fn((status: string) => Promise.resolve(status === 'open' ? pageOf([open1, open2]) : pageOf([done1]))),
      listArchived: vi.fn().mockResolvedValue(pageOf([archived1])),
      setStatus: vi.fn().mockResolvedValue(open1),
      saveComment: vi.fn().mockResolvedValue(open1),
      archive: vi.fn().mockResolvedValue(undefined),
      restore: vi.fn().mockResolvedValue(archived1),
    };
  });

  it('is titled "Meldungen" with the button "Archiv" at the top right', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Meldungen');
    expect(q('.head [data-testid="archive-button"]')!.textContent).toContain('Archiv');
  });

  it('puts the open reports at the top, highlighted yellow, and the done ones below', async () => {
    await create();

    expect(ids('[data-testid="report-row"]')).toEqual(['o1', 'o2', 'd1']);
    const block = q('[data-testid="attention"]')!;
    expect(block.textContent).toContain('Offene Meldungen');
    expect(block.contains(q('[data-id="o1"]'))).toBe(true);
    expect(q('[data-id="o1"]')!.classList.contains('attention')).toBe(true);
    expect(q('[data-id="d1"]')!.classList.contains('attention')).toBe(false);
    expect(root.querySelectorAll('[data-id="o1"]').length).toBe(1);
    expect(root.textContent).toContain('Erledigte Meldungen');
  });

  it('shows category, severity, stores, day and text of a report', async () => {
    await create();

    const text = q('[data-id="o1"]')!.textContent!;
    expect(text).toContain('Diebstahl');
    expect(text).toContain('Kritisch');
    expect(text).toContain('Augsburg, München');
    expect(text).toContain('04.11.2026');
    expect(text).toContain('Kasse stimmt nicht');
    expect(q('[data-id="o1"]')!.classList.contains('sev-critical')).toBe(true);
  });

  it('shows no yellow block when nothing is open and says so', async () => {
    api['list'].mockImplementation((status: string) => Promise.resolve(status === 'open' ? pageOf([]) : pageOf([done1])));
    await create();

    expect(q('[data-testid="attention"]')).toBeNull();
    expect(root.textContent).toContain('Keine offenen Meldungen');
  });

  it('marks a report as done and reloads', async () => {
    await create();
    api['list'].mockClear();

    await click('[data-id="o1"] [data-action="done"]');

    expect(api['setStatus']).toHaveBeenCalledWith('o1', 'done');
    expect(api['list']).toHaveBeenCalled();
  });

  it('opens a done report again', async () => {
    await create();

    await click('[data-id="d1"] [data-action="reopen"]');

    expect(api['setStatus']).toHaveBeenCalledWith('d1', 'open');
  });

  it('saves a comment', async () => {
    await create();

    const box = q<HTMLTextAreaElement>('[data-id="o1"] textarea')!;
    box.value = 'Revision informiert';
    box.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    await click('[data-id="o1"] [data-action="save-comment"]');

    expect(api['saveComment']).toHaveBeenCalledWith('o1', 'Revision informiert');
  });

  it('shows an existing comment in the editor', async () => {
    await create();

    expect(q<HTMLTextAreaElement>('[data-id="o2"] textarea')!.value).toBe('Wird geprüft');
  });

  it('archives a report and reloads', async () => {
    await create();
    api['list'].mockClear();

    await click('[data-id="d1"] [data-action="archive"]');

    expect(api['archive']).toHaveBeenCalledWith('d1');
    expect(api['list']).toHaveBeenCalled();
  });

  it('switches to the archive and back with the button', async () => {
    await create();

    await click('[data-testid="archive-button"]');

    expect(api['listArchived']).toHaveBeenCalledWith(0);
    expect(ids('[data-testid="report-row"]')).toEqual(['a1']);
    expect(q('[data-testid="attention"]')).toBeNull();
    expect(q('[data-testid="archive-button"]')!.textContent).toContain('Aktuelle Meldungen');

    await click('[data-testid="archive-button"]');

    expect(ids('[data-testid="report-row"]')).toEqual(['o1', 'o2', 'd1']);
  });

  it('restores an archived report', async () => {
    await create();
    await click('[data-testid="archive-button"]');

    await click('[data-id="a1"] [data-action="restore"]');

    expect(api['restore']).toHaveBeenCalledWith('a1');
  });

  it('loads more when there are more reports than shown', async () => {
    api['list'].mockImplementation((status: string, offset: number) =>
      Promise.resolve(status === 'open' ? (offset === 0 ? pageOf([open1], 2) : pageOf([open2], 2)) : pageOf([])),
    );
    await create();
    expect(ids('[data-testid="report-row"]')).toEqual(['o1']);
    expect(q('[data-testid="more-open"]')!.textContent).toContain('1 von 2');

    await click('[data-testid="more-open"]');

    expect(api['list']).toHaveBeenCalledWith('open', 1);
    expect(ids('[data-testid="report-row"]')).toEqual(['o1', 'o2']);
    expect(q('[data-testid="more-open"]')).toBeNull();
  });

  it('shows the answer of the portal when a change is refused', async () => {
    api['setStatus'].mockRejectedValue(envelope('Dafür fehlt dir das Recht.'));
    await create();

    await click('[data-id="o1"] [data-action="done"]');

    expect(q('[role="alert"]')!.textContent).toContain('Dafür fehlt dir das Recht.');
  });

  it('shows the reports but no actions to a person who may only read', async () => {
    await create(grant('report:read'));

    expect(ids('[data-testid="report-row"]')).toHaveLength(3);
    expect(q('[data-action="done"]')).toBeNull();
    expect(q('[data-action="reopen"]')).toBeNull();
    expect(q('[data-action="archive"]')).toBeNull();
    expect(q('textarea')).toBeNull();
    expect(root.textContent).toContain('Wird geprüft');
  });

  it('hides archiving without its privilege', async () => {
    await create(grant('report:read', 'report:update'));

    expect(q('[data-action="archive"]')).toBeNull();
    expect(q('[data-action="done"]')).not.toBeNull();
  });

  it('tells the person if the reports cannot be loaded', async () => {
    api['list'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('geladen');
  });
});
