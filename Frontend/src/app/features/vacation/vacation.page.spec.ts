import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { Absence, VacationBalance } from '../absences/absence-models';
import { AbsencesApi } from '../absences/absences.api';
import { VacationPage } from './vacation.page';

const vac = (patch: Partial<Absence>): Absence => ({
  id: 'v',
  employeeId: 'e1',
  type: 'Vacation',
  status: 'Approved',
  from: '2026-12-21',
  to: '2026-12-31',
  certificateStatus: null,
  certifiedThrough: null,
  ...patch,
});

const approved = vac({ id: 'approved' });
const requested = vac({ id: 'requested', status: 'Requested', from: '2027-03-01', to: '2027-03-05' });
const rejected = vac({ id: 'rejected', status: 'Rejected', from: '2026-08-03', to: '2026-08-07' });
const sick: Absence = { id: 'sick', employeeId: 'e1', type: 'Sickness', status: 'Reported', from: '2026-11-02', to: null, certificateStatus: 'Missing', certifiedThrough: null };
const balance: VacationBalance = { year: 2026, entitlement: 30, taken: 3, planned: 2, remaining: 25 };

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const problem = (title: string) => new HttpErrorResponse({ status: 422, error: { title } });

describe('VacationPage', () => {
  let api: Record<'list' | 'balance' | 'requestVacation' | 'withdraw' | 'holidays', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<VacationPage>;
  let root: HTMLElement;

  async function create(privileges = grant('absence:read-own', 'absence:create-own', 'absence:update-own')): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: AbsencesApi, useValue: api },
        { provide: AuthService, useValue: { privileges: signal(privileges) } },
      ],
    });
    fixture = TestBed.createComponent(VacationPage);
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

  async function type(selector: string, value: string): Promise<void> {
    const input = q<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  async function submit(): Promise<void> {
    q<HTMLFormElement>('[data-testid="request-form"]')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  beforeEach(() => {
    api = {
      list: vi.fn().mockResolvedValue([sick, approved, requested, rejected]),
      balance: vi.fn().mockResolvedValue(balance),
      requestVacation: vi.fn().mockResolvedValue(approved),
      withdraw: vi.fn().mockResolvedValue(approved),
      holidays: vi.fn().mockResolvedValue([]),
    };
  });

  it('is titled "Mein Urlaub" with the button "Urlaub beantragen" at the top right', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Mein Urlaub');
    expect(q('.head [data-testid="request-button"]')!.textContent).toContain('Urlaub beantragen');
  });

  it('shows the vacation account of the year', async () => {
    await create();

    const text = q('[data-testid="balance"]')!.textContent!;
    expect(text).toContain('30');
    expect(text).toContain('25');
  });

  it('explains a missing entitlement', async () => {
    api['balance'].mockRejectedValue(problem('NoEntitlement'));
    await create();

    expect(q('[data-testid="balance"]')!.textContent).toContain('kein Urlaubsanspruch');
  });

  it('moves the account to another year', async () => {
    await create();
    api['balance'].mockClear();

    await click('[data-action="next-year"]');

    expect(api['balance']).toHaveBeenCalledWith(new Date().getFullYear() + 1);
  });

  it('lists only vacation, no sick notes', async () => {
    await create();

    expect(ids('[data-testid="vacation-row"]').sort()).toEqual(['approved', 'rejected', 'requested']);
  });

  it('shows the period and the state', async () => {
    await create();

    const text = q('[data-id="approved"]')!.textContent!;
    expect(text).toContain('21.12.2026 – 31.12.2026');
    expect(text).toContain('Genehmigt');
    expect(q('[data-id="rejected"]')!.textContent).toContain('Abgelehnt');
  });

  it('puts requests that still wait for a decision at the top, highlighted yellow', async () => {
    await create();

    expect(ids('[data-testid="vacation-row"]')[0]).toBe('requested');
    const block = q('[data-testid="attention"]')!;
    expect(block.contains(q('[data-id="requested"]'))).toBe(true);
    expect(block.textContent).toContain('Wartet auf Genehmigung');
    expect(q('[data-id="requested"]')!.classList.contains('attention')).toBe(true);
    expect(root.querySelectorAll('[data-id="requested"]').length).toBe(1);
    expect(q('[data-id="approved"]')!.classList.contains('attention')).toBe(false);
  });

  it('shows no yellow block when nothing waits', async () => {
    api['list'].mockResolvedValue([approved, rejected]);
    await create();

    expect(q('[data-testid="attention"]')).toBeNull();
  });

  it('says so when there is no vacation yet', async () => {
    api['list'].mockResolvedValue([sick]);
    await create();

    expect(root.textContent).toContain('Noch kein Urlaub');
  });

  it('opens the form with the button and requests vacation, then reloads list and account', async () => {
    await create();
    expect(q('[data-testid="request-form"]')).toBeNull();
    api['list'].mockClear();
    api['balance'].mockClear();

    await click('[data-testid="request-button"]');
    await type('#vac-from', '2026-12-21');
    await type('#vac-to', '2026-12-31');
    await submit();

    expect(api['requestVacation']).toHaveBeenCalledWith('2026-12-21', '2026-12-31');
    expect(api['list']).toHaveBeenCalledTimes(1);
    expect(api['balance']).toHaveBeenCalledTimes(1);
    expect(q('[data-testid="status"]')!.textContent).toContain('Urlaub beantragt');
    expect(q('[data-testid="request-form"]')).toBeNull();
  });

  it('does not send a request with the end before the start', async () => {
    await create();

    await click('[data-testid="request-button"]');
    await type('#vac-from', '2026-12-31');
    await type('#vac-to', '2026-12-30');
    await submit();

    expect(api['requestVacation']).not.toHaveBeenCalled();
    expect(q('[role="alert"]')!.textContent).toContain('Ende liegt vor dem Beginn');
  });

  it('needs both days', async () => {
    await create();

    await click('[data-testid="request-button"]');
    await type('#vac-from', '2026-12-21');
    await submit();

    expect(api['requestVacation']).not.toHaveBeenCalled();
    expect(q('[role="alert"]')!.textContent).toContain('Ende');
  });

  it('shows the answer of the service in plain words and keeps the form open', async () => {
    api['requestVacation'].mockRejectedValue(problem('InsufficientBalance'));
    await create();

    await click('[data-testid="request-button"]');
    await type('#vac-from', '2026-12-21');
    await type('#vac-to', '2026-12-31');
    await submit();

    expect(q('[role="alert"]')!.textContent).toContain('Urlaubstage');
    expect(q('[data-testid="request-form"]')).not.toBeNull();
  });

  it('closes the form again with Abbrechen', async () => {
    await create();

    await click('[data-testid="request-button"]');
    await click('[data-action="cancel-request"]');

    expect(q('[data-testid="request-form"]')).toBeNull();
  });

  it('withdraws a request from its row', async () => {
    await create();
    api['list'].mockClear();

    await click('[data-id="requested"] [data-action="withdraw"]');

    expect(api['withdraw']).toHaveBeenCalledWith('requested');
    expect(api['list']).toHaveBeenCalledTimes(1);
  });

  it('offers withdraw only for requested or approved vacation', async () => {
    await create();

    expect(q('[data-id="requested"] [data-action="withdraw"]')).not.toBeNull();
    expect(q('[data-id="approved"] [data-action="withdraw"]')).not.toBeNull();
    expect(q('[data-id="rejected"] [data-action="withdraw"]')).toBeNull();
  });

  it('shows the answer of the service when withdrawing is refused', async () => {
    api['withdraw'].mockRejectedValue(problem('AlreadyStarted'));
    await create();

    await click('[data-id="approved"] [data-action="withdraw"]');

    expect(q('[role="alert"]')!.textContent).toContain('begonnen');
  });

  it('shows the list but no button and no actions to a person who may only read', async () => {
    await create(grant('absence:read-own'));

    expect(ids('[data-testid="vacation-row"]')).toHaveLength(3);
    expect(q('[data-testid="request-button"]')).toBeNull();
    expect(q('[data-action="withdraw"]')).toBeNull();
  });

  it('tells the person if the list cannot be loaded', async () => {
    api['list'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('geladen');
  });
});
