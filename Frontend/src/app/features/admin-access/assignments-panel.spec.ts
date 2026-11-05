import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { Assignment, Role, StoreGroup, dayAfter, dayStart } from './admin-access-models';
import { AssignmentsPanel } from './assignments-panel';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const ALL = ['access:assignment:read', 'access:assignment:create', 'access:assignment:delete'];

const PERSON = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const roles: Role[] = [
  { id: 'r1', name: 'Filialleitung', privileges: [], builtIn: false },
  { id: 'r2', name: 'Public User', privileges: [], builtIn: true },
];
const scopes: StoreGroup[] = [{ id: 'g1', name: 'Bezirk Süd', storeIds: [] }];

const assignment = (over: Partial<Assignment> = {}): Assignment => ({
  id: 'a1', employeeId: PERSON, roleId: 'r2', unrestricted: true, scopeIds: [], includeHomeStore: false, validFrom: null, validTo: null, source: 'Automatic', ...over,
});

describe('AssignmentsPanel', () => {
  let api: Record<'assignments' | 'roles' | 'scopes' | 'assign' | 'removeAssignment', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant(...ALL));
  let fixture: ComponentFixture<AssignmentsPanel>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(AssignmentsPanel);
    fixture.componentRef.setInput('personId', PERSON);
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

  async function click(selector: string, scope: ParentNode = root): Promise<void> {
    scope.querySelector<HTMLElement>(selector)!.click();
    await settle();
  }

  async function set(selector: string, value: string, event = 'input'): Promise<void> {
    const el = q<HTMLInputElement | HTMLSelectElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event(event, { bubbles: true }));
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant(...ALL));
    api = {
      assignments: vi.fn().mockResolvedValue([assignment()]),
      roles: vi.fn().mockResolvedValue(roles),
      scopes: vi.fn().mockResolvedValue(scopes),
      assign: vi.fn().mockResolvedValue({ id: 'new' }),
      removeAssignment: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('lists the assignments of the person with role name, reach, validity and source', async () => {
    api['assignments'].mockResolvedValue([
      assignment(),
      assignment({ id: 'a2', roleId: 'r1', source: 'Manual', unrestricted: false, scopeIds: ['g1'], includeHomeStore: true, validFrom: dayStart('2026-11-01'), validTo: dayAfter('2026-11-30') }),
    ]);
    await create();

    expect(api['assignments']).toHaveBeenCalledWith(PERSON);
    const rows = root.querySelectorAll('[data-testid="assignment-row"]');
    expect(rows[0].textContent).toContain('Public User');
    expect(rows[0].textContent).toContain('Überall');
    expect(rows[0].textContent).toContain('unbefristet');
    expect(rows[1].querySelector('[data-testid="reach"]')!.textContent).toContain('Bezirk Süd, Hauptfiliale');
    expect(rows[1].querySelector('[data-testid="period"]')!.textContent).toContain('ab 01.11.2026 bis 30.11.2026');
    expect(rows[1].textContent).toContain('Von Hand vergeben');
  });

  it('says so when the person has no assignment and when they cannot be seen', async () => {
    api['assignments'].mockResolvedValue([]);
    await create();
    expect(q('[data-testid="no-assignments"]')).not.toBeNull();
  });

  it('says that the assignments of the person are out of reach', async () => {
    api['assignments'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('nicht sehen');
  });

  it('assigns a role everywhere, with the end day sent as the start of the next day', async () => {
    await create();

    await set('[data-testid="role-select"]', 'r1', 'change');
    await set('[data-testid="to"]', '2026-11-30');
    await click('[data-action="add"]');

    expect(api['assign']).toHaveBeenCalledWith(PERSON, {
      roleId: 'r1', unrestricted: true, scopeIds: null, includeHomeStore: false, validFrom: null, validTo: dayAfter('2026-11-30'),
    });
    expect(api['assignments']).toHaveBeenCalledTimes(2);
    expect(q('[data-testid="status"]')!.textContent).toContain('zugewiesen');
  });

  it('assigns a role for chosen groups and the home store', async () => {
    await create();

    await set('[data-testid="role-select"]', 'r1', 'change');
    await click('[data-testid="reach-scopes"]');
    q<HTMLInputElement>('mat-checkbox[data-id="g1"] input')!.click();
    q<HTMLInputElement>('[data-testid="include-home"] input')!.click();
    await settle();
    await click('[data-action="add"]');

    expect(api['assign']).toHaveBeenCalledWith(PERSON, expect.objectContaining({ roleId: 'r1', unrestricted: false, scopeIds: ['g1'], includeHomeStore: true }));
  });

  it('wants a role, a reach and a sensible period before it calls the service', async () => {
    await create();

    await click('[data-action="add"]');
    expect(q('[data-testid="form-error"]')!.textContent).toContain('Rolle');

    await set('[data-testid="role-select"]', 'r1', 'change');
    await click('[data-testid="reach-scopes"]');
    await click('[data-action="add"]');
    expect(q('[data-testid="form-error"]')!.textContent).toContain('Filial-Gruppe');

    await click('[data-testid="reach-unrestricted"]');
    await set('[data-testid="from"]', '2026-11-10');
    await set('[data-testid="to"]', '2026-11-05');
    await click('[data-action="add"]');
    expect(q('[data-testid="form-error"]')!.textContent).toContain('vor');

    expect(api['assign']).not.toHaveBeenCalled();
  });

  it('explains refusals of the service', async () => {
    api['assign'].mockRejectedValue(new HttpErrorResponse({ status: 403, error: { title: 'ValidityTooLong' } }));
    await create();
    await set('[data-testid="role-select"]', 'r1', 'change');

    await click('[data-action="add"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('länger vergeben');
  });

  it('removes an assignment only after a confirmation', async () => {
    await create();

    await click('[data-action="ask-delete"]');
    expect(api['removeAssignment']).not.toHaveBeenCalled();
    await click('[data-action="cancel-delete"]');
    expect(api['removeAssignment']).not.toHaveBeenCalled();

    await click('[data-action="ask-delete"]');
    await click('[data-action="delete"]');

    expect(api['removeAssignment']).toHaveBeenCalledWith('a1');
    expect(q('[data-testid="status"]')!.textContent).toContain('entzogen');
  });

  it('says that the last administrator cannot be taken away', async () => {
    api['removeAssignment'].mockRejectedValue(new HttpErrorResponse({ status: 403, error: { title: 'LastAdministrator' } }));
    await create();
    await click('[data-action="ask-delete"]');

    await click('[data-action="delete"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('mindestens eine Person');
  });

  it('hides adding and removing without the privileges', async () => {
    privileges.set(grant('access:assignment:read'));
    await create();

    expect(q('[data-testid="add-panel"]')).toBeNull();
    expect(q('[data-action="ask-delete"]')).toBeNull();
  });

  it('cannot add when roles or groups are hidden from the person, and shows ids instead of names', async () => {
    api['roles'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();

    expect(q('[data-testid="lookups-missing"]')).not.toBeNull();
    expect(q('[data-testid="add-panel"]')).toBeNull();
    expect(q('[data-testid="assignment-row"]')!.textContent).toContain('r2');
  });
});
