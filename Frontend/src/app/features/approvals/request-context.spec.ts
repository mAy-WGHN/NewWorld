import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TeamAbsencesApi } from '../team-absences/team-absences.api';
import { StoreUnavailability } from '../team-absences/team-absences-models';
import { PendingApproval } from './approval-models';
import { ApprovalsApi } from './approvals.api';
import { ApprovalsPage } from './approvals.page';
import { RequestContext } from './request-context';

const request: PendingApproval = { absenceId: 'a1', employeeId: 'me', employeeName: 'Erika Mustermann', from: '2026-12-24', to: '2026-12-28', storeId: 's1' };

const away = (id: string, name: string, from: string, to: string, kind: string | null = 'URLAUB'): StoreUnavailability => ({
  employeeName: name,
  period: { employeeId: id, from, to, status: 'Approved', kind },
});

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 4; i++) {
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r));
  }
}

describe('RequestContext', () => {
  let store: ReturnType<typeof vi.fn>;

  async function create(req: PendingApproval = request): Promise<{ fixture: ComponentFixture<RequestContext>; root: HTMLElement }> {
    TestBed.configureTestingModule({ providers: [{ provide: TeamAbsencesApi, useValue: { store } }] });
    const fixture = TestBed.createComponent(RequestContext);
    fixture.componentRef.setInput('request', req);
    await settle(fixture);
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    store = vi.fn().mockResolvedValue([away('a', 'Anna Abwesend', '2026-12-26', '2026-12-30'), away('b', 'Ben Krank', '2026-12-25', '2026-12-25', 'KRANK')]);
  });

  it('asks for the store of the applicant with the window around the request', async () => {
    await create();

    expect(store).toHaveBeenCalledWith('s1', '2026-12-21', '2026-12-31');
  });

  it('shows the request marked in its own row, the colleagues below and says who is away', async () => {
    const { root } = await create();

    const rows = [...root.querySelectorAll('[data-testid="context-row"]')].map((r) => r.getAttribute('data-id'));
    expect(rows).toEqual(['me', 'a', 'b']);
    expect(root.querySelectorAll('[data-testid="requested-cell"]')).toHaveLength(5); // 24 to 28 December
    expect(root.querySelector('[data-testid="context-summary"]')!.textContent).toContain('2 Personen fehlen');
    const ben = root.querySelector('[data-id="b"]')!;
    expect(ben.querySelectorAll('[data-testid="context-cell"]')).toHaveLength(1);
    expect(ben.querySelector('[data-testid="context-cell"]')!.getAttribute('title')).toBe('Krank');
  });

  it('does not list the applicant twice when the store answer contains their own periods', async () => {
    store.mockResolvedValue([away('me', 'Erika Mustermann', '2026-12-24', '2026-12-28')]);
    const { root } = await create();

    expect([...root.querySelectorAll('[data-testid="context-row"]')].map((r) => r.getAttribute('data-id'))).toEqual(['me']);
    expect(root.querySelector('[data-testid="context-summary"]')!.textContent).toContain('niemand');
  });

  it('says so when the absences cannot be loaded or the store is not known', async () => {
    store.mockRejectedValue(new Error('403'));
    let result = await create();
    expect(result.root.querySelector('[data-testid="context-error"]')!.textContent).toContain('konnten nicht geladen werden');

    store.mockClear();
    TestBed.resetTestingModule();
    result = await create({ ...request, storeId: null });
    expect(result.root.querySelector('[data-testid="context-error"]')!.textContent).toContain('Filiale der Person ist nicht bekannt');
    expect(store).not.toHaveBeenCalled();
  });
});

describe('ApprovalsPage: Wer fehlt noch?', () => {
  it('opens and closes the context for a request, and offers it only when the store is known', async () => {
    const store = vi.fn().mockResolvedValue([]);
    const other: PendingApproval = { absenceId: 'a2', employeeId: 'x', employeeName: 'Ohne Filiale', from: '2027-03-01', to: '2027-03-02' };
    TestBed.configureTestingModule({
      providers: [
        { provide: ApprovalsApi, useValue: { pending: vi.fn().mockResolvedValue([request, other]), approve: vi.fn(), reject: vi.fn() } },
        { provide: TeamAbsencesApi, useValue: { store } },
      ],
    });
    const fixture = TestBed.createComponent(ApprovalsPage);
    const root = fixture.nativeElement as HTMLElement;
    await settle(fixture);

    expect(root.querySelectorAll('[data-action="context"]')).toHaveLength(1); // the second request has no store
    expect(root.querySelector('[data-testid="context"]')).toBeNull();

    root.querySelector<HTMLButtonElement>('[data-action="context"]')!.click();
    await settle(fixture);
    expect(root.querySelector('[data-testid="context"]')).not.toBeNull();
    expect(root.querySelector('[data-action="context"]')!.getAttribute('aria-expanded')).toBe('true');
    expect(store).toHaveBeenCalledTimes(1);

    root.querySelector<HTMLButtonElement>('[data-action="context"]')!.click();
    await settle(fixture);
    expect(root.querySelector('[data-testid="context"]')).toBeNull();
  });
});
