import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { CertificateDocument, SickNoteOverview, formatSize, rejectionLabel } from './sick-note-review-models';
import { SickNoteReviewApi } from './sick-note-review.api';
import { SickNoteReviewPage } from './sick-note-review.page';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

const row = (over: Partial<SickNoteOverview> = {}): SickNoteOverview => ({
  absenceId: 'a1', employeeId: 'e1', employeeName: 'Erika Mustermann', storeId: null, from: '2026-11-05', to: '2026-11-07',
  certificateStatus: 'Submitted', certifiedThrough: null, remindersSent: 0, lastReminderAt: null, kind: 'First', electronicCertificate: false, ...over,
});

const doc = (over: Partial<CertificateDocument> = {}): CertificateDocument => ({
  id: 'd1', absenceId: 'a1', state: 'Submitted', certifiedFrom: '2026-11-05', certifiedTo: '2026-11-07', uploadedAt: '2026-11-05T09:00:00Z',
  uploadedBy: 'e1', contentType: 'application/pdf', length: 2048, rejectionReason: null, hasContent: true, canOpen: true, ...over,
});

const problem = (title: string, status = 409) => new HttpErrorResponse({ status, error: { title } });

describe('SickNoteReviewPage', () => {
  let api: Record<'list' | 'documents' | 'verify' | 'reject' | 'remove' | 'content', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant('certificate:read-status', 'certificate:review', 'certificate:delete'));
  let fixture: ComponentFixture<SickNoteReviewPage>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: SickNoteReviewApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(SickNoteReviewPage);
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

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant('certificate:read-status', 'certificate:review', 'certificate:delete'));
    api = {
      list: vi.fn().mockResolvedValue({ items: [row()], total: 1, page: 1, pageSize: 25 }),
      documents: vi.fn().mockResolvedValue([doc()]),
      verify: vi.fn().mockResolvedValue({}),
      reject: vi.fn().mockResolvedValue({}),
      remove: vi.fn().mockResolvedValue(undefined),
      content: vi.fn().mockResolvedValue(new Blob(['x'], { type: 'application/pdf' })),
    };
  });

  it('starts with what waits for the check, and marks it yellow', async () => {
    await create();

    expect(api['list']).toHaveBeenCalledWith('Submitted', 1, 25);
    const r = q('[data-testid="sick-row"]')!;
    expect(r.textContent).toContain('Erika Mustermann');
    expect(r.textContent).toContain('05.11.2026 – 07.11.2026');
    expect(r.textContent).toContain('Wartet auf Prüfung');
    expect(r.classList.contains('attention')).toBe(true);
  });

  it('shows the kind of the sick note and marks an eAU instead of "AU fehlt"', async () => {
    api['list'].mockResolvedValue({ items: [row({ kind: 'FollowUp', certificateStatus: 'Missing', electronicCertificate: true })], total: 1, page: 1, pageSize: 25 });
    await create();

    const r = q('[data-testid="sick-row"]')!;
    expect(r.querySelector('[data-testid="kind"]')!.textContent).toContain('Folgekrankmeldung');
    expect(r.querySelector('[data-testid="electronic-chip"]')!.textContent).toContain('eAU');
    expect(r.textContent).not.toContain('AU fehlt');
  });

  it('asks for the sick notes marked as eAU with the filter "eAU"', async () => {
    await create();

    [...root.querySelectorAll<HTMLButtonElement>('[data-testid="filter"]')].find((b) => b.textContent!.trim() === 'eAU')!.click();
    await settle();

    expect(api['list']).toHaveBeenLastCalledWith('electronic', 1, 25);
  });

  it('does not mark rows yellow that need no check', async () => {
    api['list'].mockResolvedValue({ items: [row({ certificateStatus: 'Missing', remindersSent: 2 })], total: 1, page: 1, pageSize: 25 });
    await create();

    expect(q('[data-testid="sick-row"]')!.classList.contains('attention')).toBe(false);
    expect(q('[data-testid="sick-row"]')!.textContent).toContain('2 Erinnerungen gesendet');
  });

  it('asks again with the chosen filter, "Alle" without one', async () => {
    await create();

    const buttons = [...root.querySelectorAll<HTMLButtonElement>('[data-testid="filter"]')];
    buttons.find((b) => b.textContent!.includes('AU fehlt'))!.click();
    await settle();
    expect(api['list']).toHaveBeenLastCalledWith('Missing', 1, 25);

    buttons.find((b) => b.textContent!.includes('Alle'))!.click();
    await settle();
    expect(api['list']).toHaveBeenLastCalledWith(null, 1, 25);
  });

  it('says so when nothing has that state', async () => {
    api['list'].mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 });
    await create();

    expect(q('[data-testid="empty"]')).not.toBeNull();
  });

  it('says so when the list cannot be loaded', async () => {
    api['list'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('konnten nicht geladen werden');
  });

  it('loads more on "Mehr anzeigen" and hides it when all are shown', async () => {
    api['list']
      .mockResolvedValueOnce({ items: [row()], total: 2, page: 1, pageSize: 25 })
      .mockResolvedValueOnce({ items: [row({ absenceId: 'a2', employeeName: 'Ben Beispiel' })], total: 2, page: 2, pageSize: 25 });
    await create();

    await click('[data-action="more"]');

    expect(api['list']).toHaveBeenLastCalledWith('Submitted', 2, 25);
    expect(root.querySelectorAll('[data-testid="sick-row"]').length).toBe(2);
    expect(q('[data-action="more"]')).toBeNull();
  });

  it('shows the documents of a row when it is opened', async () => {
    await create();

    await click('[data-action="toggle"]');

    expect(api['documents']).toHaveBeenCalledWith('a1');
    const d = q('[data-testid="document"]')!;
    expect(d.textContent).toContain('Laut AU 05.11.2026 bis 07.11.2026');
    expect(d.textContent).toContain('2,0 KB');
    expect(q('[data-action="open-file"]')).not.toBeNull();
  });

  it('says that no AU is there yet', async () => {
    api['documents'].mockResolvedValue([]);
    await create();

    await click('[data-action="toggle"]');

    expect(q('[data-testid="no-documents"]')).not.toBeNull();
  });

  it('confirms a document and loads the list again', async () => {
    await create();
    await click('[data-action="toggle"]');

    await click('[data-action="verify"]');

    expect(api['verify']).toHaveBeenCalledWith('a1', 'd1');
    expect(q('[data-testid="status"]')!.textContent).toContain('AU bestätigt');
    expect(api['list']).toHaveBeenCalledTimes(2);
  });

  it('turns a document down only with a reason from the fixed list', async () => {
    await create();
    await click('[data-action="toggle"]');

    await click('[data-action="ask-reject"]');
    const select = q<HTMLSelectElement>('[data-testid="reject-panel"] select')!;
    select.value = 'WrongPeriod';
    select.dispatchEvent(new Event('change'));
    await click('[data-action="reject"]');

    expect(api['reject']).toHaveBeenCalledWith('a1', 'd1', 'WrongPeriod');
    expect(q('[data-testid="reject-panel"]')).toBeNull();
  });

  it('can cancel the rejection without a call', async () => {
    await create();
    await click('[data-action="toggle"]');

    await click('[data-action="ask-reject"]');
    await click('[data-action="cancel-reject"]');

    expect(api['reject']).not.toHaveBeenCalled();
    expect(q('[data-testid="reject-panel"]')).toBeNull();
  });

  it('asks before it deletes a file for good', async () => {
    await create();
    await click('[data-action="toggle"]');

    await click('[data-action="ask-delete"]');
    expect(api['remove']).not.toHaveBeenCalled();
    await click('[data-action="delete"]');

    expect(api['remove']).toHaveBeenCalledWith('a1', 'd1');
  });

  it('shows what the service answered if a decision fails', async () => {
    api['verify'].mockRejectedValue(problem('InvalidState'));
    await create();
    await click('[data-action="toggle"]');

    await click('[data-action="verify"]');

    expect(q('[role="alert"]')!.textContent).toContain('Das ist im aktuellen Stand nicht möglich.');
  });

  it('opens the file in a new tab as a blob', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);
    URL.createObjectURL = vi.fn().mockReturnValue('blob:x');
    await create();
    await click('[data-action="toggle"]');

    await click('[data-action="open-file"]');

    expect(api['content']).toHaveBeenCalledWith('a1', 'd1');
    expect(open).toHaveBeenCalledWith('blob:x', '_blank', 'noopener');
    open.mockRestore();
  });

  it('offers no file to open when the caller may not open it', async () => {
    api['documents'].mockResolvedValue([doc({ canOpen: false })]);
    await create();

    await click('[data-action="toggle"]');

    expect(q('[data-action="open-file"]')).toBeNull();
  });

  it('shows no decision buttons without the review privilege, nor delete without its privilege', async () => {
    privileges.set(grant('certificate:read-status'));
    await create();

    await click('[data-action="toggle"]');

    expect(q('[data-action="verify"]')).toBeNull();
    expect(q('[data-action="ask-reject"]')).toBeNull();
    expect(q('[data-action="ask-delete"]')).toBeNull();
  });

  it('offers no decision for a document that is already decided', async () => {
    api['documents'].mockResolvedValue([doc({ state: 'Verified' })]);
    await create();

    await click('[data-action="toggle"]');

    expect(q('[data-action="verify"]')).toBeNull();
  });
});

describe('sick note review models', () => {
  it('writes sizes in plain units', () => {
    expect(formatSize(500)).toBe('500 B');
    expect(formatSize(1536)).toBe('1,5 KB');
    expect(formatSize(3 * 1024 * 1024)).toBe('3,0 MB');
  });

  it('names the reasons and shows an unknown one as it is', () => {
    expect(rejectionLabel('Unreadable')).toBe('Nicht lesbar');
    expect(rejectionLabel('Something')).toBe('Something');
    expect(rejectionLabel(null)).toBe('');
  });
});
