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
  let api: Record<'list' | 'reportSickNote' | 'endSick' | 'uploadCertificate' | 'setElectronic', ReturnType<typeof vi.fn>>;
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
      reportSickNote: vi.fn().mockResolvedValue({ certificateFailed: false }),
      setElectronic: vi.fn().mockResolvedValue(sick({})),
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

  // ---------------------------------------------------------------- reporting

  const submitForm = async () => {
    q<HTMLFormElement>('[data-testid="sick-form"]')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  };

  const chooseKind = async (kind: 'first' | 'follow-up') => {
    q<HTMLInputElement>(`[data-testid="kind-${kind}"] input`)!.click();
    await settle();
  };

  const pickFile = async (selector: string, file: File) => {
    const input = q<HTMLInputElement>(selector)!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await settle();
  };

  const pdf = () => new File([new Uint8Array([1])], 'au.pdf', { type: 'application/pdf' });

  it('opens the form with the button, nothing is preselected, and there is no optional end', async () => {
    await create();
    expect(q('[data-testid="sick-form"]')).toBeNull();

    await click('[data-testid="report-button"]');

    expect(q('[data-testid="sick-form"]')).not.toBeNull();
    expect(q<HTMLInputElement>('[data-testid="kind-first"] input')!.checked).toBe(false);
    expect(q<HTMLInputElement>('[data-testid="kind-follow-up"] input')!.checked).toBe(false);
    expect(q('[data-testid="sick-form"]')!.textContent).toContain('Letzter Tag');
    expect(q('[data-testid="sick-form"]')!.textContent).not.toContain('optional');
    expect(q('#sick-file')).not.toBeNull();
    expect(q('[data-testid="electronic"]')).not.toBeNull();
  });

  it('asks for first or follow-up before it sends', async () => {
    await create();

    await click('[data-testid="report-button"]');
    await type('#sick-to', '2026-11-06');
    await submitForm();

    expect(api['reportSickNote']).not.toHaveBeenCalled();
    expect(q('[data-testid="form-error"]')!.textContent).toContain('Erst- oder Folgekrankmeldung');
  });

  it('needs the last day', async () => {
    await create();

    await click('[data-testid="report-button"]');
    await chooseKind('first');
    await type('#sick-from', '2026-11-04');
    await submitForm();

    expect(api['reportSickNote']).not.toHaveBeenCalled();
    expect(q('[data-testid="form-error"]')!.textContent).toContain('Ende');
  });

  it('does not send a sick note that ends before it starts', async () => {
    await create();

    await click('[data-testid="report-button"]');
    await chooseKind('first');
    await type('#sick-from', '2026-11-06');
    await type('#sick-to', '2026-11-04');
    await submitForm();

    expect(api['reportSickNote']).not.toHaveBeenCalled();
    expect(q('[data-testid="form-error"]')!.textContent).toContain('Ende liegt vor dem Beginn');
  });

  it('reports a first sick note, reloads the list and closes the form', async () => {
    await create();
    api['list'].mockClear();

    await click('[data-testid="report-button"]');
    await chooseKind('first');
    await type('#sick-from', '2026-11-04');
    await type('#sick-to', '2026-11-06');
    await submitForm();

    expect(api['reportSickNote']).toHaveBeenCalledWith({ kind: 'First', from: '2026-11-04', to: '2026-11-06', electronic: false, file: null });
    expect(api['list']).toHaveBeenCalledTimes(1);
    expect(q('[data-testid="status"]')!.textContent).toContain('Krankmeldung gemeldet');
    expect(q('[data-testid="sick-form"]')).toBeNull();
  });

  it('reports a follow-up and says that it needs its own AU', async () => {
    await create();

    await click('[data-testid="report-button"]');
    await chooseKind('follow-up');
    expect(q('[data-testid="follow-up-hint"]')!.textContent).toContain('eigene AU');
    await type('#sick-from', '2026-11-05');
    await type('#sick-to', '2026-11-08');
    await submitForm();

    expect(api['reportSickNote']).toHaveBeenCalledWith({ kind: 'FollowUp', from: '2026-11-05', to: '2026-11-08', electronic: false, file: null });
  });

  it('hands in the AU together with the sick note', async () => {
    await create();
    const file = pdf();

    await click('[data-testid="report-button"]');
    await chooseKind('first');
    await type('#sick-from', '2026-11-04');
    await type('#sick-to', '2026-11-06');
    await pickFile('#sick-file', file);
    await submitForm();

    expect(api['reportSickNote']).toHaveBeenCalledWith({ kind: 'First', from: '2026-11-04', to: '2026-11-06', electronic: false, file });
    expect(q('[data-testid="status"]')!.textContent).toContain('AU ist hochgeladen');
  });

  it('marks the AU as an eAU instead of a file; the later choice wins', async () => {
    await create();
    const file = pdf();

    await click('[data-testid="report-button"]');
    await chooseKind('first');
    await type('#sick-to', '2026-11-06');
    await pickFile('#sick-file', file);
    q<HTMLInputElement>('[data-testid="electronic"] input')!.click();
    await settle();
    await submitForm();

    expect(api['reportSickNote']).toHaveBeenCalledWith(expect.objectContaining({ electronic: true, file: null }));

    // the other way round: a file after "eAU" removes the mark
    await click('[data-testid="report-button"]');
    await chooseKind('first');
    await type('#sick-to', '2026-11-06');
    q<HTMLInputElement>('[data-testid="electronic"] input')!.click();
    await settle();
    await pickFile('#sick-file', file);
    await submitForm();

    expect(api['reportSickNote']).toHaveBeenLastCalledWith(expect.objectContaining({ electronic: false, file }));
  });

  it('keeps the sick note and says what to do if the AU could not be uploaded', async () => {
    api['reportSickNote'].mockResolvedValue({ certificateFailed: true, reason: 'Die Datei ist zu groß oder leer.' });
    await create();

    await click('[data-testid="report-button"]');
    await chooseKind('first');
    await type('#sick-to', '2026-11-06');
    await pickFile('#sick-file', pdf());
    await submitForm();

    expect(q('[role="alert"]')!.textContent).toContain('Die Datei ist zu groß oder leer.');
    expect(q('[role="alert"]')!.textContent).toContain('AU bearbeiten');
    expect(q('[data-testid="status"]')!.textContent).not.toContain('hochgeladen');
  });

  it('accepts only PDF, JPEG and PNG: another file is dropped at once with a message', async () => {
    await create();

    await click('[data-testid="report-button"]');
    expect(q('#sick-file')!.getAttribute('accept')).toBe('.pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg');
    await chooseKind('first');
    await type('#sick-to', '2026-11-06');
    await pickFile('#sick-file', new File(['x'], 'au.docx', { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' }));

    expect(q('[data-testid="form-error"]')!.textContent).toContain('Erlaubt sind nur PDF, JPEG und PNG.');
    await submitForm();
    expect(api['reportSickNote']).toHaveBeenCalledWith(expect.objectContaining({ file: null }));
  });

  it('takes a JPEG and a PNG, too', async () => {
    await create();
    const jpeg = new File([new Uint8Array([1])], 'photo.JPG', { type: 'image/jpeg' });
    const png = new File([new Uint8Array([1])], 'scan.png', { type: 'image/png' });

    await click('[data-testid="report-button"]');
    await chooseKind('first');
    await type('#sick-to', '2026-11-06');
    await pickFile('#sick-file', jpeg);
    expect(q('[data-testid="form-error"]')).toBeNull();
    await pickFile('#sick-file', png);
    await submitForm();

    expect(api['reportSickNote']).toHaveBeenCalledWith(expect.objectContaining({ file: png }));
  });

  it('takes an AU that is dragged onto the form, shows it, and sends it with the sick note', async () => {
    await create();
    const file = pdf();

    await click('[data-testid="report-button"]');
    await chooseKind('first');
    await type('#sick-to', '2026-11-06');
    const drop = new Event('drop', { bubbles: true, cancelable: true });
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [file] } });
    q('[data-testid="sick-form"] [data-testid="file-drop"]')!.dispatchEvent(drop);
    await settle();

    expect(q('[data-testid="sick-form"] [data-testid="file-name"]')!.textContent).toContain('au.pdf');
    await submitForm();
    expect(api['reportSickNote']).toHaveBeenCalledWith(expect.objectContaining({ file }));
  });

  it('can remove the chosen AU again', async () => {
    await create();

    await click('[data-testid="report-button"]');
    await pickFile('#sick-file', pdf());
    await click('[data-testid="sick-form"] [data-testid="remove-file"]');

    expect(q('[data-testid="sick-form"] [data-testid="file-name"]')).toBeNull();
  });

  it('also refuses another file when an AU is handed in afterwards', async () => {
    await create();

    await click('[data-id="missing"] [data-action="certificate"]');
    await pickFile('#cert-file', new File(['x'], 'au.exe', { type: 'application/x-msdownload' }));

    expect(q('[data-testid="editor-error"]')!.textContent).toContain('Erlaubt sind nur PDF, JPEG und PNG.');
    await click('[data-id="missing"] [data-testid="certificate-editor"] [data-action="confirm"]');
    expect(api['uploadCertificate']).not.toHaveBeenCalled();
  });

  it('shows the answer of the service in plain words and keeps the form open', async () => {
    api['reportSickNote'].mockRejectedValue(problem('NoPredecessor'));
    await create();

    await click('[data-testid="report-button"]');
    await chooseKind('follow-up');
    await type('#sick-to', '2026-11-06');
    await submitForm();

    expect(q('[role="alert"]')!.textContent).toContain('direkt an eine vorherige Krankmeldung anschließen');
    expect(q('[data-testid="sick-form"]')).not.toBeNull();
  });

  it('closes the form again with Abbrechen', async () => {
    await create();

    await click('[data-testid="report-button"]');
    await click('[data-action="cancel-report"]');

    expect(q('[data-testid="sick-form"]')).toBeNull();
  });

  // ---------------------------------------------------------------- AU bearbeiten

  it('hands in an AU afterwards from the highlighted note', async () => {
    await create();
    const file = pdf();

    await click('[data-id="missing"] [data-action="certificate"]');
    await pickFile('#cert-file', file);
    await type('#cert-from', '2026-10-01');
    await type('#cert-to', '2026-10-05');
    await click('[data-id="missing"] [data-testid="certificate-editor"] [data-action="confirm"]');

    expect(api['uploadCertificate']).toHaveBeenCalledWith('missing', file, '2026-10-01', '2026-10-05');
  });

  it('asks for the file before handing in an AU', async () => {
    await create();

    await click('[data-id="missing"] [data-action="certificate"]');
    await type('#cert-from', '2026-10-01');
    await type('#cert-to', '2026-10-05');
    await click('[data-id="missing"] [data-testid="certificate-editor"] [data-action="confirm"]');

    expect(api['uploadCertificate']).not.toHaveBeenCalled();
    expect(q('[data-testid="editor-error"]')!.textContent).toContain('Datei');
  });

  it('says afterwards that the AU is an eAU', async () => {
    await create();

    await click('[data-id="missing"] [data-action="certificate"]');
    q<HTMLInputElement>('[data-id="missing"] [data-testid="electronic"] input')!.click();
    await settle();

    expect(api['setElectronic']).toHaveBeenCalledWith('missing', true);
    expect(q('[data-testid="status"]')!.textContent).toContain('eAU');
  });

  it('takes the eAU mark back', async () => {
    api['list'].mockResolvedValue([sick({ id: 'missing', from: '2026-10-01', to: null, certificateStatus: 'Missing', electronicCertificate: true })]);
    await create();

    await click('[data-id="missing"] [data-action="certificate"]');
    expect(q<HTMLInputElement>('[data-id="missing"] [data-testid="electronic"] input')!.checked).toBe(true);
    q<HTMLInputElement>('[data-id="missing"] [data-testid="electronic"] input')!.click();
    await settle();

    expect(api['setElectronic']).toHaveBeenCalledWith('missing', false);
    expect(q('[data-testid="status"]')!.textContent).toContain('entfernt');
  });

  it('shows an eAU as such and does not ask for an AU for it', async () => {
    api['list'].mockResolvedValue([sick({ id: 'e', certificateStatus: 'Missing', electronicCertificate: true, certifiedThrough: null })]);
    await create();

    expect(q('[data-id="e"] [data-testid="electronic-chip"]')!.textContent).toContain('eAU');
    expect(q('[data-id="e"]')!.textContent).not.toContain('AU fehlt');
    expect(q('[data-testid="attention"]')).toBeNull();
  });

  it('never takes the option to hand in an AU away, not even for confirmed or eAU notes', async () => {
    api['list'].mockResolvedValue([
      sick({ id: 'ok', certificateStatus: 'Verified' }),
      sick({ id: 'e', from: '2026-09-01', certificateStatus: 'Missing', electronicCertificate: true }),
    ]);
    await create();

    expect(q('[data-id="ok"] [data-action="certificate"]')).not.toBeNull();
    expect(q('[data-id="e"] [data-action="certificate"]')).not.toBeNull();
  });

  it('cannot mark an eAU while an AU waits for the check', async () => {
    api['list'].mockResolvedValue([sick({ id: 'w', certificateStatus: 'Submitted' })]);
    await create();

    await click('[data-id="w"] [data-action="certificate"]');

    expect(q('[data-testid="electronic-locked"]')).not.toBeNull();
    expect(q('[data-id="w"] [data-testid="electronic"]')).toBeNull();
  });

  it('shows whether it is a first or a follow-up sick note', async () => {
    api['list'].mockResolvedValue([
      sick({ id: 'f', sickNoteKind: 'FollowUp' }),
      sick({ id: 'o', from: '2026-09-01', sickNoteKind: 'First' }),
      sick({ id: 'old', from: '2026-08-01', sickNoteKind: 'Unknown' }),
    ]);
    await create();

    expect(q('[data-id="f"] [data-testid="kind-chip"]')!.textContent).toContain('Folgekrankmeldung');
    expect(q('[data-id="o"] [data-testid="kind-chip"]')!.textContent).toContain('Erstkrankmeldung');
    expect(q('[data-id="old"] [data-testid="kind-chip"]')).toBeNull();
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
    expect(q('[data-action="certificate"]')).toBeNull();
    expect(q('[data-action="end"]')).toBeNull();
  });

  it('hides the AU upload without its privilege', async () => {
    await create(grant('absence:read-own', 'absence:create-own', 'absence:update-own'));

    expect(q('[data-action="certificate"]')).toBeNull();
    expect(q('[data-action="end"]')).not.toBeNull();
  });

  it('tells the person if the list cannot be loaded', async () => {
    api['list'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(q('[role="alert"]')!.textContent).toContain('geladen');
  });
});
