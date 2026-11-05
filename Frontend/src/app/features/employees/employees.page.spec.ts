import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { LocationsApi } from '../report-issue/locations.api';
import { ChangeRequest, emptyForm, employeeErrorMessage, toRequest, validateForm } from './employee-models';
import { EmployeesApi } from './employees.api';
import { EmployeesPage } from './employees.page';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const problem = (title: string, status = 409) => new HttpErrorResponse({ status, error: { title } });

const request = (over: Partial<ChangeRequest> = {}): ChangeRequest => ({
  id: 'r1', employeeId: 'e1', personnelNumber: '1234', personName: 'Erika Mustermann', status: 'Pending',
  fields: { street: 'Neue Str. 1', postalCode: null, city: 'Augsburg', privatePhone: null, privateEmail: null },
  message: 'Ich bin umgezogen.', createdAt: '2026-11-05T09:00:00Z', decidedAt: null, rejectionReason: null, ...over,
});

describe('EmployeesPage', () => {
  let api: Record<'changeRequests' | 'approve' | 'reject' | 'list' | 'create' | 'card', ReturnType<typeof vi.fn>>;
  let locations: { list: ReturnType<typeof vi.fn> };
  const privileges = signal(grant('employee:decide-change', 'employee:create', 'employee:read'));
  let fixture: ComponentFixture<EmployeesPage>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [{ provide: EmployeesApi, useValue: api }, { provide: LocationsApi, useValue: locations }, { provide: AuthService, useValue: { privileges } }],
    });
    fixture = TestBed.createComponent(EmployeesPage);
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

  async function type(selector: string, value: string, event = 'input'): Promise<void> {
    const el = q<HTMLInputElement | HTMLSelectElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event(event));
    await settle();
  }

  async function fillRequired(): Promise<void> {
    await type('#f-firstName', 'Erika');
    await type('#f-lastName', 'Mustermann');
    await type('#f-gender', 'Female', 'change');
    await type('#f-hireDate', '2026-12-01');
  }

  beforeEach(() => {
    privileges.set(grant('employee:decide-change', 'employee:create', 'employee:read'));
    api = {
      changeRequests: vi.fn().mockResolvedValue([request()]),
      approve: vi.fn().mockResolvedValue(undefined),
      reject: vi.fn().mockResolvedValue(undefined),
      list: vi.fn().mockResolvedValue([
        { id: 'f1', kind: 'Function', code: 'A', name: 'Verkäufer', isActive: true },
        { id: 'f2', kind: 'Function', code: 'B', name: 'Alt', isActive: false },
      ]),
      create: vi.fn().mockResolvedValue({ employeeId: '123e4567-e89b-12d3-a456-426614174001', personnelNumber: '15302' }),
      card: vi.fn(),
    };
    locations = { list: vi.fn().mockResolvedValue([{ id: 's1', name: 'Augsburg', storeCode: '101', archived: false }, { id: 's2', name: 'Alt', storeCode: '102', archived: true }]) };
  });

  describe('tabs', () => {
    it('shows every tab the person has the privilege for', async () => {
      await create();

      expect([...root.querySelectorAll('[data-testid="tab"]')].map((t) => t.textContent!.trim())).toEqual(['Änderungsanträge', 'Neue Person anlegen', 'Karte ansehen']);
    });

    it('hides tabs without the privilege and starts on the first one left', async () => {
      privileges.set(grant('employee:create'));
      await create();

      expect(q('[data-tab="requests"]')).toBeNull();
      expect(q('[data-tab="card"]')).toBeNull();
      expect(q('#f-firstName')).not.toBeNull();
      expect(api['changeRequests']).not.toHaveBeenCalled();
    });

    it('says so when no privilege fits', async () => {
      privileges.set(grant());
      await create();

      expect(root.textContent).toContain('keine Berechtigung');
    });
  });

  describe('change requests', () => {
    it('shows person, changed fields only and the message, marked yellow', async () => {
      await create();

      const r = q('[data-testid="request"]')!;
      expect(r.textContent).toContain('Erika Mustermann');
      expect(r.textContent).toContain('1234');
      expect([...r.querySelectorAll('[data-testid="changed-field"]')].map((f) => f.textContent!.trim())).toEqual(['Straße neu: Neue Str. 1', 'Ort neu: Augsburg']);
      expect(r.querySelector('[data-testid="message"]')!.textContent).toContain('Ich bin umgezogen.');
      expect(r.classList.contains('attention')).toBe(true);
    });

    it('says so when nothing is open', async () => {
      api['changeRequests'].mockResolvedValue([]);
      await create();

      expect(q('[data-testid="no-requests"]')).not.toBeNull();
    });

    it('approves with one click and loads the list again', async () => {
      await create();

      await click('[data-action="approve"]');

      expect(api['approve']).toHaveBeenCalledWith('r1');
      expect(api['changeRequests']).toHaveBeenCalledTimes(2);
      expect(q('[data-testid="status"]')!.textContent).toContain('genehmigt');
    });

    it('does not reject without the extra confirmation step', async () => {
      await create();

      await click('[data-action="ask-reject"]');

      expect(api['reject']).not.toHaveBeenCalled();
      expect(q('[data-testid="reject-panel"]')).not.toBeNull();
    });

    it('rejects with the reason after the confirmation', async () => {
      await create();
      await click('[data-action="ask-reject"]');

      await type('[data-testid="reject-panel"] input', ' Adresse stimmt nicht ');
      await click('[data-action="reject"]');

      expect(api['reject']).toHaveBeenCalledWith('r1', 'Adresse stimmt nicht');
      expect(q('[data-testid="reject-panel"]')).toBeNull();
    });

    it('rejects without a reason as null, and can cancel without a call', async () => {
      await create();
      await click('[data-action="ask-reject"]');
      await click('[data-action="cancel-reject"]');
      expect(api['reject']).not.toHaveBeenCalled();

      await click('[data-action="ask-reject"]');
      await click('[data-action="reject"]');
      expect(api['reject']).toHaveBeenCalledWith('r1', null);
    });

    it('says in plain words when the decision fails', async () => {
      api['approve'].mockRejectedValue(problem('NotPending'));
      await create();

      await click('[data-action="approve"]');

      expect(q('[data-testid="error"]')!.textContent).toContain('schon entschieden');
    });

    it('says so when the list cannot be loaded', async () => {
      api['changeRequests'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
      await create();

      expect(q('[role="alert"]')!.textContent).toContain('konnten nicht geladen werden');
    });
  });

  describe('new person', () => {
    beforeEach(() => privileges.set(grant('employee:create', 'employee:read')));

    it('offers only active entries and open stores', async () => {
      await create();

      const options = [...root.querySelectorAll('#f-functionId option')].map((o) => o.textContent!.trim());
      expect(options).toContain('Verkäufer');
      expect(options).not.toContain('Alt');
      expect([...root.querySelectorAll('#f-mainStoreId option')].map((o) => o.textContent!.trim()).join()).not.toContain('Alt');
    });

    it('asks for the wage type list and shows it only with its privilege', async () => {
      await create();
      expect(q('#f-wageTypeId')).toBeNull();
      expect(api['list']).not.toHaveBeenCalledWith('wage-type');
    });

    it('shows the wage type with its privilege', async () => {
      privileges.set(grant('employee:create', 'employee:read-wage-type'));
      await create();

      expect(q('#f-wageTypeId')).not.toBeNull();
      expect(api['list']).toHaveBeenCalledWith('wage-type');
    });

    it('does not send an incomplete form and names the missing fields', async () => {
      await create();

      await click('[data-action="create"]');

      expect(api['create']).not.toHaveBeenCalled();
      const messages = [...root.querySelectorAll('[data-testid="field-error"]')].map((e) => e.textContent);
      expect(messages).toEqual(expect.arrayContaining(['Bitte den Vornamen eintragen.', 'Bitte den Nachnamen eintragen.', 'Bitte das Geschlecht wählen.', 'Bitte das Eintrittsdatum eintragen.']));
    });

    it('sends only the filled fields and shows the assigned number', async () => {
      await create();
      await fillRequired();
      await type('#f-functionId', 'f1', 'change');

      await click('[data-action="create"]');

      const sent = api['create'].mock.calls[0][0];
      expect(sent).toMatchObject({ firstName: 'Erika', lastName: 'Mustermann', gender: 'Female', hireDate: '2026-12-01', functionId: 'f1', street: null, wageTypeId: null, vacationDaysPerYear: null });
      expect(q('[data-testid="personnel-number"]')!.textContent).toBe('15302');
      expect(q('[data-testid="created"]')!.textContent).toContain('Business Central');
    });

    it('links the created person to the card', async () => {
      api['card'].mockResolvedValue({ employeeId: '123e4567-e89b-12d3-a456-426614174001', personnelNumber: '15302', firstName: 'Erika', lastName: 'Mustermann', receiptName: 'Erika M.', hireDate: '2026-12-01', state: 'PendingBc', private: null, wage: null });
      await create();
      await fillRequired();
      await click('[data-action="create"]');

      await click('[data-action="open-created"]');

      expect(api['card']).toHaveBeenCalledWith('123e4567-e89b-12d3-a456-426614174001');
      expect(q('[data-testid="card"]')!.textContent).toContain('Erika Mustermann');
    });

    it('maps the errors of the service', async () => {
      api['create'].mockRejectedValue(problem('NumberSeriesNotSet'));
      await create();
      await fillRequired();

      await click('[data-action="create"]');

      expect(q('[data-testid="error"]')!.textContent).toContain('Nummernkreis');
      expect(q('[data-testid="created"]')).toBeNull();
    });
  });

  describe('card', () => {
    beforeEach(() => privileges.set(grant('employee:read')));

    it('asks for a valid ID first', async () => {
      await create();
      await type('#card-id', 'abc');

      await click('[data-action="load-card"]');

      expect(api['card']).not.toHaveBeenCalled();
      expect(q('[data-testid="card-error"]')!.textContent).toContain('gültige Mitarbeiter-ID');
    });

    it('shows the private part and the wage only if the answer has them', async () => {
      api['card'].mockResolvedValue({
        employeeId: 'e1', personnelNumber: '1234', firstName: 'Erika', lastName: 'Mustermann', receiptName: 'Erika M.', hireDate: '2026-01-01', state: 'Synced',
        private: { gender: 'Female', birthDate: null, street: 'Weg 1', postalCode: '86150', city: 'Augsburg', privatePhone: null, privateEmail: null }, wage: null,
      });
      await create();
      await type('#card-id', '123e4567-e89b-12d3-a456-426614174000');

      await click('[data-action="load-card"]');

      expect(api['card']).toHaveBeenCalledWith('123e4567-e89b-12d3-a456-426614174000');
      expect(q('[data-testid="private"]')!.textContent).toContain('Weg 1');
      expect(q('[data-testid="wage"]')).toBeNull();
    });

    it('says so when there is no card or no permission', async () => {
      api['card'].mockRejectedValue(new HttpErrorResponse({ status: 404 }));
      await create();
      await type('#card-id', '123e4567-e89b-12d3-a456-426614174000');

      await click('[data-action="load-card"]');

      expect(q('[data-testid="card-error"]')!.textContent).toContain('keine Karte');
    });
  });
});

describe('employee models', () => {
  it('validates required fields, e-mail, dates and vacation days', () => {
    const form = { ...emptyForm(), firstName: 'A', lastName: 'B', gender: 'Male', hireDate: '2026-05-01', exitDate: '2026-04-01', privateEmail: 'x', vacationDaysPerYear: '400' };

    expect(Object.keys(validateForm(form)).sort()).toEqual(['exitDate', 'privateEmail', 'vacationDaysPerYear']);
    expect(validateForm({ ...emptyForm(), firstName: 'A', lastName: 'B', gender: 'Male', hireDate: '2026-05-01', vacationDaysPerYear: '25,5' })).toEqual({});
  });

  it('turns empty fields into null, a decimal comma into a number, and drops the wage type without privilege', () => {
    const form = { ...emptyForm(), firstName: ' A ', lastName: 'B', gender: 'Male', hireDate: '2026-05-01', vacationDaysPerYear: '25,5', wageTypeId: 'w1' };

    expect(toRequest(form, false)).toMatchObject({ firstName: 'A', street: null, vacationDaysPerYear: 25.5, wageTypeId: null });
    expect(toRequest(form, true).wageTypeId).toBe('w1');
  });

  it('names the codes of the service and falls back to a generic text', () => {
    expect(employeeErrorMessage('DuplicateNumber')).toContain('Personalnummer');
    expect(employeeErrorMessage('UnknownSupervisor')).toContain('Vorgesetzten');
    expect(employeeErrorMessage('Whatever')).toContain('nicht geklappt');
    expect(employeeErrorMessage(undefined)).toContain('nicht geklappt');
  });
});
