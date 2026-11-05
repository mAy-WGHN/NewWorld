import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { ChangeRequest, EmployeeCard, buildInput, formFromCard, requestErrorMessage, validateInput } from './employee-card-models';
import { EmployeeCardApi } from './employee-card.api';
import { EmployeeCardPage } from './employee-card.page';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

const card = (over: Partial<EmployeeCard> = {}): EmployeeCard => ({
  employeeId: 'e1', personnelNumber: '15302', firstName: 'Erika', lastName: 'Mustermann', receiptName: 'Erika M.', businessEmail: 'erika@firma.de',
  hireDate: '2025-03-01', exitDate: null,
  private: { gender: 'Female', birthDate: '1990-05-17', street: 'Hauptstr. 1', postalCode: '86150', city: 'Augsburg', privatePhone: '0821 123456', privateEmail: 'erika@privat.de' },
  ...over,
});

const request = (over: Partial<ChangeRequest> = {}): ChangeRequest => ({
  id: 'r1', employeeId: 'e1', status: 'Pending', fields: { street: 'Neue Str. 2', postalCode: null, city: null, privatePhone: null, privateEmail: null },
  message: null, createdAt: '2026-11-05T09:00:00Z', decidedAt: null, rejectionReason: null, ...over,
});

const problem = (title: string, status = 409) => new HttpErrorResponse({ status, error: { title } });

describe('EmployeeCardPage', () => {
  let api: Record<'card' | 'requests' | 'request' | 'withdraw', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant('employee:read-own', 'employee:request-change-own'));
  let fixture: ComponentFixture<EmployeeCardPage>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: EmployeeCardApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(EmployeeCardPage);
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

  async function type(selector: string, value: string): Promise<void> {
    const input = q<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  async function click(selector: string): Promise<void> {
    q<HTMLButtonElement>(selector)!.click();
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant('employee:read-own', 'employee:request-change-own'));
    api = {
      card: vi.fn().mockResolvedValue(card()),
      requests: vi.fn().mockResolvedValue([]),
      request: vi.fn().mockResolvedValue(request()),
      withdraw: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('shows the own data, the private data and never an id or the wage type', async () => {
    await create();

    expect(q('[data-testid="name"]')!.textContent).toContain('Erika Mustermann');
    expect(q('[data-testid="personnel-number"]')!.textContent).toContain('15302');
    const text = root.textContent!;
    expect(text).toContain('Erika M.');
    expect(text).toContain('erika@firma.de');
    expect(text).toContain('01.03.2025');
    expect(q('[data-testid="private"]')!.textContent).toContain('Hauptstr. 1');
    expect(q('[data-testid="private"]')!.textContent).toContain('17.05.1990');
    expect(q('[data-testid="private"]')!.textContent).toContain('Weiblich');
    expect(text).not.toContain('Lohnart');
    expect(q('[data-testid="exit-date"]')).toBeNull();
  });

  it('says so in plain words when the person has no card (404), without an error', async () => {
    api['card'].mockResolvedValue(null);
    await create();

    expect(q('[data-testid="no-card"]')).not.toBeNull();
    expect(q('[role="alert"]')).toBeNull();
    expect(q('[data-testid="form"]')).toBeNull();
    expect(api['requests']).not.toHaveBeenCalled();
  });

  it('says so when the card cannot be loaded', async () => {
    api['card'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('konnte nicht geladen werden');
    expect(q('[data-testid="no-card"]')).toBeNull();
  });

  it('hides the request form and does not load requests without the privilege', async () => {
    privileges.set(grant('employee:read-own'));
    await create();

    expect(q('[data-testid="card"]')).not.toBeNull();
    expect(q('[data-testid="request-section"]')).toBeNull();
    expect(api['requests']).not.toHaveBeenCalled();
  });

  it('prefills the form and sends only the changed fields', async () => {
    await create();
    expect(q<HTMLInputElement>('#ec-street')!.value).toBe('Hauptstr. 1');

    await type('#ec-street', 'Neue Str. 2');
    await click('[data-action="submit"]');

    expect(api['request']).toHaveBeenCalledWith({ street: 'Neue Str. 2' });
    expect(q('[data-testid="status"]')!.textContent).toContain('Antrag wurde gesendet');
    expect(api['card']).toHaveBeenCalledTimes(2);
  });

  it('sends the free message alone', async () => {
    await create();

    await type('#ec-message', 'Ich habe geheiratet, neuer Name: Muster');
    await click('[data-action="submit"]');

    expect(api['request']).toHaveBeenCalledWith({ message: 'Ich habe geheiratet, neuer Name: Muster' });
  });

  it('does not send anything when nothing changed, and says why', async () => {
    await create();

    await click('[data-action="submit"]');

    expect(api['request']).not.toHaveBeenCalled();
    expect(q('[role="alert"]')!.textContent).toContain('mindestens ein Feld');
  });

  it('does not send an invalid e-mail address', async () => {
    await create();

    await type('#ec-email', 'kein-mail');
    await click('[data-action="submit"]');

    expect(api['request']).not.toHaveBeenCalled();
    expect(q('[role="alert"]')!.textContent).toContain('E-Mail');
  });

  it('explains in plain language that only one request can be open (409)', async () => {
    api['request'].mockRejectedValue(problem('PendingRequestExists'));
    await create();

    await type('#ec-city', 'München');
    await click('[data-action="submit"]');

    expect(q('[role="alert"]')!.textContent).toContain('schon einen offenen Antrag');
  });

  it('explains it when nobody in HR is responsible', async () => {
    api['request'].mockRejectedValue(problem('NoHandler'));
    await create();

    await type('#ec-city', 'München');
    await click('[data-action="submit"]');

    expect(q('[role="alert"]')!.textContent).toContain('Personalabteilung');
  });

  it('shows a pending request instead of the form and can withdraw it', async () => {
    api['requests'].mockResolvedValue([request()]);
    await create();

    expect(q('[data-testid="pending"]')).not.toBeNull();
    expect(q('[data-testid="form"]')).toBeNull();

    api['requests'].mockResolvedValue([request({ status: 'Withdrawn' })]);
    await click('[data-action="withdraw"]');

    expect(api['withdraw']).toHaveBeenCalledWith('r1');
    expect(q('[data-testid="status"]')!.textContent).toContain('zurückgezogen');
    expect(q('[data-testid="form"]')).not.toBeNull();
  });

  it('says so when withdrawing fails because the request is already decided', async () => {
    api['requests'].mockResolvedValue([request()]);
    api['withdraw'].mockRejectedValue(problem('NotPending'));
    await create();

    await click('[data-action="withdraw"]');

    expect(q('[role="alert"]')!.textContent).toContain('schon entschieden');
  });

  it('lists the requests with status and the reason of a rejection', async () => {
    api['requests'].mockResolvedValue([
      request({ id: 'r2', status: 'Rejected', rejectionReason: 'Nachweis fehlt', createdAt: '2026-10-01T09:00:00Z' }),
      request({ id: 'r3', status: 'Approved', createdAt: '2026-09-01T09:00:00Z', fields: { street: null, postalCode: null, city: 'Ulm', privatePhone: null, privateEmail: null } }),
    ]);
    await create();

    const rows = root.querySelectorAll('[data-testid="request-row"]');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Abgelehnt');
    expect(rows[0].textContent).toContain('Grund: Nachweis fehlt');
    expect(rows[1].textContent).toContain('Übernommen');
    expect(rows[1].textContent).toContain('Ort: Ulm');
    expect(q('[data-testid="form"]')).not.toBeNull();
  });
});

describe('employee card models', () => {
  it('builds the request from changed, non-empty fields only', () => {
    const form = { ...formFromCard(card()), city: ' München ', privatePhone: '', message: '  ' };
    expect(buildInput(form, card())).toEqual({ city: 'München' });
  });

  it('validates like the service', () => {
    expect(validateInput({})).not.toBeNull();
    expect(validateInput({ privatePhone: 'abc' })).toContain('Telefonnummer');
    expect(validateInput({ privatePhone: '+49 821 123' })).toBeNull();
    expect(validateInput({ postalCode: '12345678901' })).toContain('PLZ');
    expect(validateInput({ message: 'x'.repeat(501) })).toContain('500');
    expect(validateInput({ privateEmail: 'a@b.de' })).toBeNull();
  });

  it('maps error titles and falls back for unknown ones', () => {
    expect(requestErrorMessage('PendingRequestExists', 'x')).toContain('offenen Antrag');
    expect(requestErrorMessage('Whatever', 'Fallback')).toBe('Fallback');
    expect(requestErrorMessage(undefined, 'Fallback')).toBe('Fallback');
  });
});
