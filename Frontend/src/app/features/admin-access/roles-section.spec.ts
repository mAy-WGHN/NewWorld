import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { ADMINISTRATOR_ROLE_ID, CatalogEntry, Role } from './admin-access-models';
import { RolesSection } from './roles-section';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const ALL = ['access:role:read', 'access:role:create', 'access:role:update', 'access:role:delete'];

const admin: Role = { id: ADMINISTRATOR_ROLE_ID, name: 'Administrator', privileges: ['*'], builtIn: true };
const publicUser: Role = { id: 'p1', name: 'Public User', privileges: ['absence:create-own'], builtIn: true };
const lead: Role = { id: 'l1', name: 'Filialleitung', privileges: ['absence:approve'], builtIn: false };

const catalog: CatalogEntry[] = [
  { key: 'absence:create-own', owner: 'workforce', scopable: false, description: 'Eigene Abwesenheiten anlegen.' },
  { key: 'absence:approve', owner: 'workforce', scopable: true, description: 'Urlaub genehmigen.' },
  { key: 'certificate:review', owner: 'workforce', scopable: true, description: 'AU prüfen.' },
];

const problem = (title: string, status = 403) => new HttpErrorResponse({ status, error: { title } });

describe('RolesSection', () => {
  let api: Record<'roles' | 'catalog' | 'createRole' | 'updateRole' | 'deleteRole', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant(...ALL));
  let fixture: ComponentFixture<RolesSection>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(RolesSection);
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
  const row = (id: string) => q(`[data-testid="role-row"][data-id="${id}"]`)!;

  async function click(selector: string, scope: ParentNode = root): Promise<void> {
    scope.querySelector<HTMLElement>(selector)!.click();
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const input = q<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function tick(key: string): Promise<void> {
    q<HTMLInputElement>(`mat-checkbox[data-key="${key}"] input`)!.click();
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant(...ALL));
    api = {
      roles: vi.fn().mockResolvedValue([admin, publicUser, lead]),
      catalog: vi.fn().mockResolvedValue(catalog),
      createRole: vi.fn().mockResolvedValue({ id: 'new' }),
      updateRole: vi.fn().mockResolvedValue(undefined),
      deleteRole: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('lists the roles with their number of privileges and marks the built-in ones', async () => {
    await create();

    const names = [...root.querySelectorAll('[data-testid="role-row"] strong')].map((e) => e.textContent!.trim());
    expect(names).toEqual(['Administrator', 'Public User', 'Filialleitung']);
    expect(row(lead.id).textContent).toContain('1 Recht');
    expect(row(admin.id).querySelector('[data-testid="built-in-chip"]')).not.toBeNull();
    expect(row(lead.id).querySelector('[data-testid="built-in-chip"]')).toBeNull();
  });

  it('offers deletion only for roles that are not built in', async () => {
    await create();

    expect(row(admin.id).querySelector('[data-action="ask-delete"]')).toBeNull();
    expect(row(publicUser.id).querySelector('[data-action="ask-delete"]')).toBeNull();
    expect(row(lead.id).querySelector('[data-action="ask-delete"]')).not.toBeNull();
  });

  it('shows the administrator role read-only: no way to save', async () => {
    await create();

    await click('[data-action="edit"]', row(admin.id));

    expect(row(admin.id).querySelector('[data-action="save"]')).toBeNull();
    expect(row(admin.id).querySelector<HTMLInputElement>('mat-checkbox[data-key="*"] input')!.disabled).toBe(true);
    expect(row(admin.id).textContent).toContain('Alle Rechte');
  });

  it('shows the descriptions grouped by area and filters them', async () => {
    await create();
    await click('[data-action="edit"]', row(lead.id));

    expect([...root.querySelectorAll('[data-area]')].map((e) => e.getAttribute('data-area'))).toEqual(['absence', 'certificate']);
    expect(q('[data-testid="picker"]')!.textContent).toContain('Urlaub genehmigen.');

    await type('[data-testid="picker-search"]', 'prüfen');
    expect([...root.querySelectorAll('[data-area]')].map((e) => e.getAttribute('data-area'))).toEqual(['certificate']);

    await type('[data-testid="picker-search"]', 'gibtesnicht');
    expect(q('[data-testid="no-match"]')).not.toBeNull();
  });

  it('saves the changed privileges of a role, wildcards included, and only when something changed', async () => {
    await create();
    await click('[data-action="edit"]', row(lead.id));
    expect(q<HTMLButtonElement>('[data-action="save"]')!.disabled).toBe(true);

    await tick('certificate:review');
    await tick('absence:*');
    await click('[data-action="save"]');

    expect(api['updateRole']).toHaveBeenCalledWith(lead.id, ['absence:approve', 'certificate:review', 'absence:*']);
    expect(api['roles']).toHaveBeenCalledTimes(2);
    expect(q('[data-testid="status"]')!.textContent).toContain('gespeichert');
    expect(q('[data-testid="editor"]')).toBeNull();
  });

  it('lists a privilege that left the catalogue so that it can be removed', async () => {
    api['roles'].mockResolvedValue([{ ...lead, privileges: ['absence:approve', 'gone:privilege'] }]);
    await create();
    await click('[data-action="edit"]', row(lead.id));

    expect(q('[data-testid="unknown"]')!.textContent).toContain('gone:privilege');
    await click('[data-action="remove-unknown"]');
    await click('[data-action="save"]');

    expect(api['updateRole']).toHaveBeenCalledWith(lead.id, ['absence:approve']);
  });

  it('explains a refused change in plain words and keeps the editor open', async () => {
    api['updateRole'].mockRejectedValue(problem('NoEscalation'));
    await create();
    await click('[data-action="edit"]', row(lead.id));
    await tick('certificate:review');

    await click('[data-action="save"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('nicht mehr Rechte');
    expect(q('[data-testid="editor"]')).not.toBeNull();
  });

  it('creates a role with a name and privileges', async () => {
    await create();
    await click('[data-action="open-create"]');

    await type('[data-testid="new-name"]', ' Revisor ');
    await tick('certificate:review');
    await click('[data-action="create"]');

    expect(api['createRole']).toHaveBeenCalledWith('Revisor', ['certificate:review']);
    expect(q('[data-testid="status"]')!.textContent).toContain('angelegt');
    expect(q('[data-testid="create-panel"]')).toBeNull();
  });

  it('does not create a role without a name or with a name that exists', async () => {
    await create();
    await click('[data-action="open-create"]');

    await click('[data-action="create"]');
    expect(q('[data-testid="name-error"]')!.textContent).toContain('Namen');

    await type('[data-testid="new-name"]', 'filialleitung');
    await click('[data-action="create"]');

    expect(q('[data-testid="name-error"]')!.textContent).toContain('schon');
    expect(api['createRole']).not.toHaveBeenCalled();
  });

  it('shows the message of the service when creating fails', async () => {
    api['createRole'].mockRejectedValue(problem('UnknownPrivilege', 422));
    await create();
    await click('[data-action="open-create"]');
    await type('[data-testid="new-name"]', 'Revisor');

    await click('[data-action="create"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('Katalog');
    expect(q('[data-testid="create-panel"]')).not.toBeNull();
  });

  it('deletes only after a confirmation, and can cancel', async () => {
    await create();
    await click('[data-action="ask-delete"]', row(lead.id));
    expect(api['deleteRole']).not.toHaveBeenCalled();

    await click('[data-action="cancel-delete"]');
    expect(q('[data-testid="delete-panel"]')).toBeNull();
    expect(api['deleteRole']).not.toHaveBeenCalled();

    await click('[data-action="ask-delete"]', row(lead.id));
    await click('[data-action="delete"]');

    expect(api['deleteRole']).toHaveBeenCalledWith(lead.id);
    expect(q('[data-testid="status"]')!.textContent).toContain('gelöscht');
  });

  it('says that a role that is still assigned cannot be deleted', async () => {
    api['deleteRole'].mockRejectedValue(problem('InUse', 409));
    await create();
    await click('[data-action="ask-delete"]', row(lead.id));

    await click('[data-action="delete"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('verwendet');
  });

  it('hides create, change and delete without their privileges', async () => {
    privileges.set(grant('access:role:read'));
    await create();

    expect(q('[data-action="open-create"]')).toBeNull();
    expect(row(lead.id).querySelector('[data-action="ask-delete"]')).toBeNull();
    expect(row(lead.id).querySelector('[data-action="edit"]')!.textContent).toContain('ansehen');
    await click('[data-action="edit"]', row(lead.id));
    expect(q('[data-action="save"]')).toBeNull();
    expect(q<HTMLInputElement>('mat-checkbox[data-key="absence:approve"] input')!.disabled).toBe(true);
  });

  it('says so when the roles cannot be loaded, and when the catalogue is hidden from the person', async () => {
    api['roles'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    api['catalog'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('nicht geladen');
    expect(q('[data-testid="catalog-missing"]')).not.toBeNull();
    expect(q('[data-action="open-create"]')).toBeNull();
  });
});
