import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { Absence, VacationBalance } from './absence-models';
import { AbsencesApi } from './absences.api';
import { AbsencesPage } from './absences.page';

const vacation: Absence = { id: 'v1', employeeId: 'e1', type: 'Vacation', status: 'Approved', from: '2026-12-21', to: '2026-12-31', certificateStatus: null, certifiedThrough: null };
const sick: Absence = { id: 's1', employeeId: 'e1', type: 'Sickness', status: 'Reported', from: '2026-11-02', to: null, certificateStatus: 'Missing', certifiedThrough: null };
const balance: VacationBalance = { year: 2026, entitlement: 30, taken: 3, planned: 2, remaining: 25 };

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const problem = (title: string, status = 422) => new HttpErrorResponse({ status, error: { title } });

describe('AbsencesPage', () => {
  let api: Record<keyof AbsencesApi, ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<AbsencesPage>;
  let root: HTMLElement;

  async function create(privileges = grant('absence:read-own', 'absence:create-own', 'absence:update-own', 'certificate:upload-own')): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: AbsencesApi, useValue: api },
        { provide: AuthService, useValue: { privileges: signal(privileges) } },
      ],
    });
    fixture = TestBed.createComponent(AbsencesPage);
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
  const rows = () => root.querySelectorAll('[data-testid="absence-row"]');

  async function type(selector: string, value: string): Promise<void> {
    const input = q<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function submit(selector: string): Promise<void> {
    q<HTMLFormElement>(selector)!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  beforeEach(() => {
    api = {
      list: vi.fn().mockResolvedValue([vacation, sick]),
      balance: vi.fn().mockResolvedValue(balance),
      reportSick: vi.fn().mockResolvedValue(sick),
      requestVacation: vi.fn().mockResolvedValue(vacation),
      withdraw: vi.fn().mockResolvedValue(vacation),
      endSick: vi.fn().mockResolvedValue(sick),
      uploadCertificate: vi.fn().mockResolvedValue({}),
    };
  });

  it('shows the absences with state and the vacation account', async () => {
    await create();

    expect(rows().length).toBe(2);
    expect(root.textContent).toContain('Urlaub');
    expect(root.textContent).toContain('21.12.2026 – 31.12.2026');
    expect(root.textContent).toContain('Genehmigt');
    expect(root.textContent).toContain('Krankmeldung');
    expect(root.textContent).toContain('ab 02.11.2026 (offen)');
    expect(root.textContent).toContain('AU fehlt');
    expect(q('[data-testid="balance"]')!.textContent).toContain('25');
  });

  it('says so when nothing was reported yet', async () => {
    api['list'].mockResolvedValue([]);
    await create();

    expect(rows().length).toBe(0);
    expect(root.textContent).toContain('Keine Abwesenheiten');
  });

  it('explains a missing entitlement instead of showing an empty account', async () => {
    api['balance'].mockRejectedValue(problem('NoEntitlement'));
    await create();

    expect(q('[data-testid="balance"]')!.textContent).toContain('kein Urlaubsanspruch');
    expect(rows().length).toBe(2);
  });

  it('moves the vacation account to another year', async () => {
    await create();
    api['balance'].mockClear();

    await click('[data-action="next-year"]');

    expect(api['balance']).toHaveBeenCalledWith(new Date().getFullYear() + 1);
  });

  it('reports a sick note without an end and reloads the list', async () => {
    await create();
    api['list'].mockClear();

    await type('#sick-from', '2026-11-04');
    await submit('[data-testid="sick-form"]');

    expect(api['reportSick']).toHaveBeenCalledWith('2026-11-04', null);
    expect(api['list']).toHaveBeenCalledTimes(1);
    expect(q('[data-testid="status"]')!.textContent).toContain('Krankmeldung');
  });

  it('reports a sick note with an end', async () => {
    await create();

    await type('#sick-from', '2026-11-04');
    await type('#sick-to', '2026-11-06');
    await submit('[data-testid="sick-form"]');

    expect(api['reportSick']).toHaveBeenCalledWith('2026-11-04', '2026-11-06');
  });

  it('does not send a vacation request with the end before the start', async () => {
    await create();

    await type('#vac-from', '2026-12-31');
    await type('#vac-to', '2026-12-30');
    await submit('[data-testid="vacation-form"]');

    expect(api['requestVacation']).not.toHaveBeenCalled();
    expect(q('[role="alert"]')!.textContent).toContain('Ende liegt vor dem Beginn');
  });

  it('requests vacation and reloads list and account', async () => {
    await create();
    api['list'].mockClear();
    api['balance'].mockClear();

    await type('#vac-from', '2026-12-21');
    await type('#vac-to', '2026-12-31');
    await submit('[data-testid="vacation-form"]');

    expect(api['requestVacation']).toHaveBeenCalledWith('2026-12-21', '2026-12-31');
    expect(api['list']).toHaveBeenCalledTimes(1);
    expect(api['balance']).toHaveBeenCalledTimes(1);
  });

  it('shows the answer of the service in plain words when a request is refused', async () => {
    api['requestVacation'].mockRejectedValue(problem('InsufficientBalance'));
    await create();

    await type('#vac-from', '2026-12-21');
    await type('#vac-to', '2026-12-31');
    await submit('[data-testid="vacation-form"]');

    expect(q('[role="alert"]')!.textContent).toContain('Urlaubstage');
  });

  it('withdraws vacation from the row', async () => {
    await create();
    api['list'].mockClear();

    await click('[data-action="withdraw"]');

    expect(api['withdraw']).toHaveBeenCalledWith('v1');
    expect(api['list']).toHaveBeenCalledTimes(1);
  });

  it('offers withdraw only for vacation, end and upload only for sick notes', async () => {
    await create();

    expect(root.querySelectorAll('[data-action="withdraw"]').length).toBe(1);
    expect(root.querySelectorAll('[data-action="end"]').length).toBe(1);
    expect(root.querySelectorAll('[data-action="upload"]').length).toBe(1);
  });

  it('ends an open sick note with the last day', async () => {
    await create();

    await click('[data-action="end"]');
    await type('#end-to', '2026-11-06');
    await click('[data-action="confirm"]');

    expect(api['endSick']).toHaveBeenCalledWith('s1', '2026-11-06');
  });

  it('uploads a certificate with the period it covers', async () => {
    await create();
    const file = new File([new Uint8Array([1])], 'au.pdf', { type: 'application/pdf' });

    await click('[data-action="upload"]');
    const input = q<HTMLInputElement>('#cert-file')!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await type('#cert-from', '2026-11-02');
    await type('#cert-to', '2026-11-06');
    await click('[data-action="confirm"]');

    expect(api['uploadCertificate']).toHaveBeenCalledWith('s1', file, '2026-11-02', '2026-11-06');
  });

  it('asks for the file before uploading', async () => {
    await create();

    await click('[data-action="upload"]');
    await type('#cert-from', '2026-11-02');
    await type('#cert-to', '2026-11-06');
    await click('[data-action="confirm"]');

    expect(api['uploadCertificate']).not.toHaveBeenCalled();
    expect(q('[role="alert"]')!.textContent).toContain('Datei');
  });

  it('shows the list but no forms and no row actions to a person who may only read', async () => {
    await create(grant('absence:read-own'));

    expect(rows().length).toBe(2);
    expect(q('[data-testid="sick-form"]')).toBeNull();
    expect(q('[data-testid="vacation-form"]')).toBeNull();
    expect(q('[data-action="withdraw"]')).toBeNull();
    expect(q('[data-action="end"]')).toBeNull();
    expect(q('[data-action="upload"]')).toBeNull();
  });

  it('hides the certificate upload without its privilege', async () => {
    await create(grant('absence:read-own', 'absence:create-own', 'absence:update-own'));

    expect(q('[data-action="upload"]')).toBeNull();
    expect(q('[data-action="withdraw"]')).not.toBeNull();
  });

  it('tells the person if the list cannot be loaded', async () => {
    api['list'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('geladen');
  });
});
