import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AccessLogEntry } from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';
import { CertificatesSection } from './certificates-section';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const retention = { retentionAfterEndDays: 365, isDefault: true, changedAt: null, changedBy: null };
const reminders = { firstReminderAfterReturnDays: 0, reminderEveryDays: 3, maxReminders: 5, isDefault: true, changedAt: null, changedBy: null };
const entry = (n: number, over: Partial<AccessLogEntry> = {}): AccessLogEntry => ({
  id: `l${n}`, at: '2026-11-05T09:00:00Z', actorId: 'hr1', action: 'Downloaded', absenceId: 'a1', documentId: 'd1', employeeId: 'e1', ...over,
});

type Methods = 'retention' | 'saveRetention' | 'reminders' | 'saveReminders' | 'accessLog';

describe('CertificatesSection', () => {
  let api: Record<Methods, ReturnType<typeof vi.fn>>;
  const privileges = signal(grant('certificate:manage-retention', 'certificate:read-access-log'));
  let fixture: ComponentFixture<CertificatesSection>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminHrApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(CertificatesSection);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);

  async function type(selector: string, value: string): Promise<void> {
    const el = q<HTMLInputElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function click(selector: string): Promise<void> {
    q<HTMLElement>(selector)!.click();
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant('certificate:manage-retention', 'certificate:read-access-log'));
    api = {
      retention: vi.fn().mockResolvedValue(retention),
      saveRetention: vi.fn().mockImplementation(async (days: number) => ({ retentionAfterEndDays: days, isDefault: false, changedAt: '2026-11-05T09:30:00+00:00', changedBy: 'hr1' })),
      reminders: vi.fn().mockResolvedValue(reminders),
      saveReminders: vi.fn().mockImplementation(async (r) => ({ ...r, isDefault: false, changedAt: '2026-11-05T09:30:00+00:00', changedBy: 'hr1' })),
      accessLog: vi.fn().mockResolvedValue({ items: [entry(1), entry(2, { action: 'Verified' })], total: 2, page: 1, pageSize: 50 }),
    };
  });

  it('shows the retention and the reminder settings with the defaults', async () => {
    await create();

    expect((q('[data-testid="retention-days"]') as HTMLInputElement).value).toBe('365');
    expect(q('[data-testid="retention-audit"]')!.textContent).toContain('Standard');
    expect((q('[data-testid="first-days"]') as HTMLInputElement).value).toBe('0');
    expect((q('[data-testid="every-days"]') as HTMLInputElement).value).toBe('3');
    expect((q('[data-testid="max-reminders"]') as HTMLInputElement).value).toBe('5');
  });

  it('saves the retention in days', async () => {
    await create();
    await type('[data-testid="retention-days"]', '730');
    await click('[data-action="save-retention"]');

    expect(api['saveRetention']).toHaveBeenCalledWith(730);
    expect(q('[data-testid="retention-status"]')!.textContent).toContain('gespeichert');
    expect(q('[data-testid="retention-audit"]')!.textContent).toContain('Zuletzt geändert');
  });

  it('does not save a retention outside 0 to 3650 days', async () => {
    await create();
    await type('[data-testid="retention-days"]', '4000');
    await click('[data-action="save-retention"]');

    expect(api['saveRetention']).not.toHaveBeenCalled();
    expect(q('[data-testid="retention-error"]')!.textContent).toContain('0 bis 3650');
  });

  it('saves the reminders as typed, 0 reminders switch them off', async () => {
    await create();
    await type('[data-testid="first-days"]', '1');
    await type('[data-testid="every-days"]', '7');
    await type('[data-testid="max-reminders"]', '0');
    await click('[data-action="save-reminders"]');

    expect(api['saveReminders']).toHaveBeenCalledWith({ firstReminderAfterReturnDays: 1, reminderEveryDays: 7, maxReminders: 0 });
    expect(q('[data-testid="reminders-status"]')!.textContent).toContain('gespeichert');
  });

  it('needs an interval of at least one day', async () => {
    await create();
    await type('[data-testid="every-days"]', '0');
    await click('[data-action="save-reminders"]');

    expect(api['saveReminders']).not.toHaveBeenCalled();
    expect(q('[data-testid="reminders-error"]')!.textContent).toContain('1 bis 90');
  });

  it('shows the error of the service in plain language', async () => {
    api['saveRetention'].mockRejectedValue(new HttpErrorResponse({ status: 422, error: { title: 'InvalidPeriod' } }));
    await create();
    await click('[data-action="save-retention"]');

    expect(q('[data-testid="retention-error"]')!.textContent).toContain('nicht gültig');
  });

  it('lists the access log with plain actions and loads from/to as whole days', async () => {
    await create();

    const rows = [...root.querySelectorAll('[data-testid="log-row"]')];
    expect(rows.map((r) => r.querySelector('strong')!.textContent)).toEqual(['Geöffnet', 'Bestätigt']);
    expect(api['accessLog']).toHaveBeenCalledWith(null, null, 1, 50);

    await type('[data-testid="log-from"]', '2026-11-01');
    await type('[data-testid="log-to"]', '2026-11-05');
    await click('[data-action="filter-log"]');
    const [from, to] = api['accessLog'].mock.calls.at(-1)!;
    expect(new Date(from).getTime()).toBe(new Date('2026-11-01T00:00:00').getTime());
    expect(new Date(to).getTime()).toBe(new Date('2026-11-05T23:59:59.999').getTime());
  });

  it('loads more log pages when there are more', async () => {
    api['accessLog'].mockResolvedValueOnce({ items: [entry(1)], total: 2, page: 1, pageSize: 1 }).mockResolvedValueOnce({ items: [entry(2)], total: 2, page: 2, pageSize: 1 });
    await create();
    await click('[data-action="more-log"]');

    expect(api['accessLog']).toHaveBeenLastCalledWith(null, null, 2, 50);
    expect(root.querySelectorAll('[data-testid="log-row"]')).toHaveLength(2);
    expect(q('[data-action="more-log"]')).toBeNull();
  });

  it('says so when the log is empty or cannot be loaded', async () => {
    api['accessLog'].mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 });
    await create();
    expect(q('[data-testid="log-empty"]')).not.toBeNull();

    api['accessLog'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await click('[data-action="filter-log"]');
    expect(q('[data-testid="log-error"]')!.textContent).toContain('Laden hat nicht geklappt');
  });

  it('with only the retention privilege shows no access log and does not load it', async () => {
    privileges.set(grant('certificate:manage-retention'));
    await create();

    expect(q('[data-testid="retention-days"]')).not.toBeNull();
    expect(q('[data-testid="log-from"]')).toBeNull();
    expect(api['accessLog']).not.toHaveBeenCalled();
  });

  it('with only the log privilege shows no settings and does not load them', async () => {
    privileges.set(grant('certificate:read-access-log'));
    await create();

    expect(q('[data-testid="retention-days"]')).toBeNull();
    expect(q('[data-testid="first-days"]')).toBeNull();
    expect(api['retention']).not.toHaveBeenCalled();
    expect(api['reminders']).not.toHaveBeenCalled();
    expect(q('[data-testid="log-row"]')).not.toBeNull();
  });
});
