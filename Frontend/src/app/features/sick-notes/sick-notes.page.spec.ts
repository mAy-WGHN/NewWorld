import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { Absence } from '../absences/absence-models';
import { AbsencesApi } from '../absences/absences.api';
import { SickNotesPage } from './sick-notes.page';

const sick = (patch: Partial<Absence>): Absence => ({
  id: 's',
  employeeId: 'e1',
  type: 'Sickness',
  status: 'Reported',
  from: '2026-11-02',
  to: '2026-11-04',
  certificateStatus: 'Verified',
  certifiedThrough: '2026-11-04',
  ...patch,
});

const verified = sick({ id: 'verified', from: '2026-11-20', to: '2026-11-22' });
const missing = sick({ id: 'missing', from: '2026-10-01', to: null, certificateStatus: 'Missing', certifiedThrough: null });
const rejected = sick({ id: 'rejected', from: '2026-10-15', to: '2026-10-18', certificateStatus: 'Rejected' });
const vacation: Absence = { id: 'v', employeeId: 'e1', type: 'Vacation', status: 'Approved', from: '2026-12-21', to: '2026-12-31', certificateStatus: null, certifiedThrough: null };

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const problem = (title: string) => new HttpErrorResponse({ status: 422, error: { title } });

describe('SickNotesPage', () => {
  let api: Record<'list' | 'reportSick' | 'endSick' | 'uploadCertificate', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<SickNotesPage>;
  let root: HTMLElement;

  async function create(privileges = grant('absence:read-own', 'absence:create-own', 'absence:update-own', 'certificate:upload-own')): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: AbsencesApi, useValue: api },
        { provide: AuthService, useValue: { privileges: signal(privileges) } },
      ],
    });
    fixture = TestBed.createComponent(SickNotesPage);
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

  beforeEach(() => {
    api = {
      list: vi.fn().mockResolvedValue([vacation, verified, missing]),
      reportSick: vi.fn().mockResolvedValue(sick({})),
      endSick: vi.fn().mockResolvedValue(sick({})),
      uploadCertificate: vi.fn().mockResolvedValue({}),
    };
  });

  it('is titled "Meine Krankmeldungen" with the button "Krankmelden" at the top right', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Meine Krankmeldungen');
    const button = q('.head [data-testid="report-button"]')!;
    expect(button.textContent).toContain('Krankmelden');
  });

  it('lists only the own sick notes, no vacation', async () => {
    await create();

    expect(ids('[data-testid="sick-row"]').sort()).toEqual(['missing', 'verified']);
    expect(root.textContent).not.toContain('Urlaub');
  });

  it('shows the period, the state and where the AU stands', async () => {
    await create();

    const text = q('[data-id="verified"]')!.textContent!;
    expect(text).toContain('20.11.2026 – 22.11.2026');
    expect(text).toContain('Gemeldet');
    expect(text).toContain('AU geprüft');
  });

  it('puts a sick note whose AU is still needed at the very top, highlighted yellow', async () => {
    await create();

    const rows = ids('[data-testid="sick-row"]');
    expect(rows[0]).toBe('missing');
    const attention = q('[data-testid="attention"]')!;
    expect(attention.contains(q('[data-id="missing"]'))).toBe(true);
    expect(q('[data-id="missing"]')!.classList.contains('attention')).toBe(true);
    expect(attention.textContent).toContain('AU');
    // it appears once only, not again in the list below
    expect(root.querySelectorAll('[data-id="missing"]').length).toBe(1);
    expect(q('[data-id="verified"]')!.classList.contains('attention')).toBe(false);
  });

  it('also highlights a sick note whose AU was turned down, and says so', async () => {
    api['list'].mockResolvedValue([verified, rejected, missing]);
    await create();

    expect(ids('[data-testid="attention"] [data-testid="sick-row"]')).toEqual(['rejected', 'missing']);
    expect(q('[data-id="rejected"]')!.textContent).toContain('abgelehnt');
  });

  it('does not highlight an AU that was handed in and waits for HR', async () => {
    api['list'].mockResolvedValue([sick({ id: 'waiting', certificateStatus: 'Submitted' })]);
    await create();

    expect(q('[data-testid="attention"]')).toBeNull();
    expect(q('[data-id="waiting"]')).not.toBeNull();
  });

  it('shows no yellow block when nothing is missing', async () => {
    api['list'].mockResolvedValue([verified]);
    await create();

    expect(q('[data-testid="attention"]')).toBeNull();
  });

  it('lists the others newest first', async () => {
    api['list'].mockResolvedValue([sick({ id: 'old', from: '2026-01-05' }), sick({ id: 'new', from: '2027-01-03' }), sick({ id: 'mid', from: '2026-12-30' })]);
    await create();

    expect(ids('[data-testid="sick-row"]')).toEqual(['new', 'mid', 'old']);
  });

  it('says so when there are no sick notes', async () => {
    api['list'].mockResolvedValue([vacation]);
    await create();

    expect(root.textContent).toContain('Noch keine Krankmeldungen');
  });

  it('opens the form with the button and reports a sick note without an end', async () => {
    await create();
    expect(q('[data-testid="report-form"]')).toBeNull();
    api['list'].mockClear();

    await click('[data-testid="report-button"]');
    await type('#sick-from', '2026-11-04');
    q<HTMLFormElement>('[data-testid="report-form"]')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(api['reportSick']).toHaveBeenCalledWith('2026-11-04', null);
    expect(api['list']).toHaveBeenCalledTimes(1);
    expect(q('[data-testid="status"]')!.textContent).toContain('Krankmeldung');
    expect(q('[data-testid="report-form"]')).toBeNull();
  });

  it('reports a sick note with an end', async () => {
    await create();

    await click('[data-testid="report-button"]');
    await type('#sick-from', '2026-11-04');
    await type('#sick-to', '2026-11-06');
    q<HTMLFormElement>('[data-testid="report-form"]')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(api['reportSick']).toHaveBeenCalledWith('2026-11-04', '2026-11-06');
  });

  it('does not send a sick note that ends before it starts', async () => {
    await create();

    await click('[data-testid="report-button"]');
    await type('#sick-from', '2026-11-06');
    await type('#sick-to', '2026-11-04');
    q<HTMLFormElement>('[data-testid="report-form"]')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(api['reportSick']).not.toHaveBeenCalled();
    expect(q('[role="alert"]')!.textContent).toContain('Ende liegt vor dem Beginn');
  });

  it('shows the answer of the service in plain words and keeps the form open', async () => {
    api['reportSick'].mockRejectedValue(problem('Overlap'));
    await create();

    await click('[data-testid="report-button"]');
    await type('#sick-from', '2026-11-04');
    q<HTMLFormElement>('[data-testid="report-form"]')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();

    expect(q('[role="alert"]')!.textContent).toContain('überschneidet');
    expect(q('[data-testid="report-form"]')).not.toBeNull();
  });

  it('closes the form again with Abbrechen', async () => {
    await create();

    await click('[data-testid="report-button"]');
    await click('[data-action="cancel-report"]');

    expect(q('[data-testid="report-form"]')).toBeNull();
  });

  it('hands in the AU from the highlighted note', async () => {
    await create();
    const file = new File([new Uint8Array([1])], 'au.pdf', { type: 'application/pdf' });

    await click('[data-id="missing"] [data-action="upload"]');
    const input = q<HTMLInputElement>('#cert-file')!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await type('#cert-from', '2026-10-01');
    await type('#cert-to', '2026-10-05');
    await click('[data-action="confirm"]');

    expect(api['uploadCertificate']).toHaveBeenCalledWith('missing', file, '2026-10-01', '2026-10-05');
  });

  it('asks for the file before handing in an AU', async () => {
    await create();

    await click('[data-id="missing"] [data-action="upload"]');
    await type('#cert-from', '2026-10-01');
    await type('#cert-to', '2026-10-05');
    await click('[data-action="confirm"]');

    expect(api['uploadCertificate']).not.toHaveBeenCalled();
    expect(q('[role="alert"]')!.textContent).toContain('Datei');
  });

  it('ends an open sick note with its last day', async () => {
    await create();

    await click('[data-id="missing"] [data-action="end"]');
    await type('#end-to', '2026-10-09');
    await click('[data-action="confirm"]');

    expect(api['endSick']).toHaveBeenCalledWith('missing', '2026-10-09');
  });

  it('offers End only for open sick notes', async () => {
    await create();

    expect(q('[data-id="missing"] [data-action="end"]')).not.toBeNull();
    expect(q('[data-id="verified"] [data-action="end"]')).toBeNull();
  });

  it('shows the list but no button, no form and no actions to a person who may only read', async () => {
    await create(grant('absence:read-own'));

    expect(ids('[data-testid="sick-row"]')).toHaveLength(2);
    expect(q('[data-testid="report-button"]')).toBeNull();
    expect(q('[data-action="upload"]')).toBeNull();
    expect(q('[data-action="end"]')).toBeNull();
  });

  it('hides the AU upload without its privilege', async () => {
    await create(grant('absence:read-own', 'absence:create-own', 'absence:update-own'));

    expect(q('[data-action="upload"]')).toBeNull();
    expect(q('[data-action="end"]')).not.toBeNull();
  });

  it('tells the person if the list cannot be loaded', async () => {
    api['list'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('geladen');
  });
});
