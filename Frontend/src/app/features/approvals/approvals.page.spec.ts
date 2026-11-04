import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApprovalsApi } from './approvals.api';
import { ApprovalsPage } from './approvals.page';
import { PendingApproval } from './approval-models';

const erika: PendingApproval = { absenceId: 'a1', employeeId: 'e1', employeeName: 'Erika Mustermann', from: '2026-12-21', to: '2026-12-31' };
const ben: PendingApproval = { absenceId: 'a2', employeeId: 'e2', employeeName: 'Ben Beispiel', from: '2027-03-01', to: '2027-03-05' };

const problem = (title: string, status = 409) => new HttpErrorResponse({ status, error: { title } });

describe('ApprovalsPage', () => {
  let api: Record<'pending' | 'approve' | 'reject', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<ApprovalsPage>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: ApprovalsApi, useValue: api }] });
    fixture = TestBed.createComponent(ApprovalsPage);
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
  const ids = () => [...root.querySelectorAll('[data-testid="request-row"]')].map((e) => e.getAttribute('data-id'));

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  beforeEach(() => {
    api = {
      pending: vi.fn().mockResolvedValue([erika, ben]),
      approve: vi.fn().mockResolvedValue({}),
      reject: vi.fn().mockResolvedValue({}),
    };
  });

  it('is titled "Genehmigungen" with "Aktualisieren" at the top right', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Genehmigungen');
    expect(q('.head [data-testid="refresh"]')!.textContent).toContain('Aktualisieren');
  });

  it('lists the open requests in a yellow block, with the name and the period', async () => {
    await create();

    expect(ids()).toEqual(['a1', 'a2']);
    const block = q('[data-testid="attention"]')!;
    expect(block.textContent).toContain('Wartet auf deine Entscheidung');
    expect(block.textContent).toContain('2');
    expect(block.contains(q('[data-id="a1"]'))).toBe(true);
    expect(q('[data-id="a1"]')!.classList.contains('attention')).toBe(true);
    const text = q('[data-id="a1"]')!.textContent!;
    expect(text).toContain('Erika Mustermann');
    expect(text).toContain('21.12.2026 – 31.12.2026');
  });

  it('says that everything is done when nothing waits', async () => {
    api['pending'].mockResolvedValue([]);
    await create();

    expect(q('[data-testid="attention"]')).toBeNull();
    expect(root.textContent).toContain('Keine offenen Anträge');
  });

  it('approves a request, says so with the name and loads the list again', async () => {
    await create();
    api['pending'].mockClear();
    api['pending'].mockResolvedValue([ben]);

    await click('[data-id="a1"] [data-action="approve"]');

    expect(api['approve']).toHaveBeenCalledWith('a1');
    expect(q('[data-testid="status"]')!.textContent).toContain('Erika Mustermann');
    expect(q('[data-testid="status"]')!.textContent).toContain('genehmigt');
    expect(api['pending']).toHaveBeenCalledTimes(1);
    expect(ids()).toEqual(['a2']);
  });

  it('asks again before rejecting and rejects only after the confirmation', async () => {
    await create();

    await click('[data-id="a1"] [data-action="reject"]');
    expect(api['reject']).not.toHaveBeenCalled();
    expect(q('[data-id="a1"]')!.textContent).toContain('Wirklich ablehnen');

    await click('[data-id="a1"] [data-action="confirm-reject"]');

    expect(api['reject']).toHaveBeenCalledWith('a1');
    expect(q('[data-testid="status"]')!.textContent).toContain('abgelehnt');
  });

  it('does not reject if the person thinks better of it', async () => {
    await create();

    await click('[data-id="a1"] [data-action="reject"]');
    await click('[data-id="a1"] [data-action="cancel-reject"]');

    expect(api['reject']).not.toHaveBeenCalled();
    expect(q('[data-id="a1"] [data-action="approve"]')).not.toBeNull();
    expect(q('[data-id="a1"]')!.textContent).not.toContain('Wirklich ablehnen');
  });

  it('asks about one request at a time', async () => {
    await create();

    await click('[data-id="a1"] [data-action="reject"]');
    await click('[data-id="a2"] [data-action="reject"]');

    expect(q('[data-id="a1"]')!.textContent).not.toContain('Wirklich ablehnen');
    expect(q('[data-id="a2"]')!.textContent).toContain('Wirklich ablehnen');
  });

  it('says in plain words that somebody else decided already and shows the current list', async () => {
    api['approve'].mockRejectedValue(problem('InvalidState'));
    await create();
    api['pending'].mockClear();
    api['pending'].mockResolvedValue([ben]);

    await click('[data-id="a1"] [data-action="approve"]');

    expect(q('[role="alert"]')!.textContent).toContain('schon entschieden');
    expect(api['pending']).toHaveBeenCalledTimes(1);
    expect(ids()).toEqual(['a2']);
  });

  it('says when the person may not decide this request', async () => {
    api['approve'].mockRejectedValue(problem('NotAllowed', 403));
    await create();

    await click('[data-id="a1"] [data-action="approve"]');

    expect(q('[role="alert"]')!.textContent).toContain('darfst');
    expect(ids()).toEqual(['a1', 'a2']);
  });

  it('says when the request is gone', async () => {
    api['reject'].mockRejectedValue(problem('NotFound', 404));
    await create();

    await click('[data-id="a1"] [data-action="reject"]');
    await click('[data-id="a1"] [data-action="confirm-reject"]');

    expect(q('[role="alert"]')!.textContent).toContain('nicht gefunden');
  });

  it('loads the list again with Aktualisieren', async () => {
    await create();
    api['pending'].mockClear();

    await click('[data-testid="refresh"]');

    expect(api['pending']).toHaveBeenCalledTimes(1);
  });

  it('tells the person if the list cannot be loaded', async () => {
    api['pending'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('geladen');
  });
});
