import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { ADMINISTRATOR_ROLE_ID, CatalogEntry, Role } from './admin-access-models';
import { MatrixSection } from './matrix-section';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

const admin: Role = { id: ADMINISTRATOR_ROLE_ID, name: 'Administrator', privileges: ['*'], builtIn: true };
const publicUser: Role = { id: 'p1', name: 'Public User', privileges: ['absence:create-own'], builtIn: true };
const lead: Role = { id: 'l1', name: 'Filialleitung', privileges: ['absence:approve'], builtIn: false };
const hr: Role = { id: 'h1', name: 'HR', privileges: ['absence:*'], builtIn: false };

const catalog: CatalogEntry[] = [
  { key: 'absence:create-own', owner: 'workforce', scopable: false, description: 'Eigene Abwesenheiten anlegen.' },
  { key: 'absence:approve', owner: 'workforce', scopable: true, description: 'Urlaub genehmigen.' },
  { key: 'certificate:review', owner: 'workforce', scopable: true, description: 'AU prüfen.' },
];

const problem = (title: string, status = 403) => new HttpErrorResponse({ status, error: { title } });

describe('MatrixSection', () => {
  let api: Record<'roles' | 'catalog' | 'updateRole', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant('access:role:read', 'access:role:update', 'access:catalog:read'));
  let fixture: ComponentFixture<MatrixSection>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(MatrixSection);
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
  const cell = (roleId: string, key: string) => q<HTMLInputElement>(`input[data-role="${roleId}"][data-key="${key}"]`)!;

  async function click(selector: string): Promise<void> {
    q<HTMLElement>(selector)!.click();
    await settle();
  }

  async function clickCell(roleId: string, key: string): Promise<void> {
    cell(roleId, key).click();
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant('access:role:read', 'access:role:update', 'access:catalog:read'));
    api = {
      roles: vi.fn().mockResolvedValue([admin, publicUser, lead, hr]),
      catalog: vi.fn().mockResolvedValue(catalog),
      updateRole: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('shows every role as a column and the privileges grouped by area as rows', async () => {
    await create();

    const heads = [...root.querySelectorAll('th[scope="col"][data-role]')].map((e) => e.getAttribute('data-role'));
    expect(heads).toEqual([ADMINISTRATOR_ROLE_ID, 'p1', 'l1', 'h1']); // built-in first, then by name
    const rows = [...root.querySelectorAll('tbody tr')].map((e) => e.getAttribute('data-row'));
    expect(rows).toEqual(['*', 'absence:*', 'absence:approve', 'absence:create-own', 'certificate:*', 'certificate:review']);
    expect(q('tbody tr[data-row="absence:approve"]')!.textContent).toContain('Urlaub genehmigen.');
  });

  it('marks what a role has directly and what it only has through a collective privilege', async () => {
    await create();

    expect(cell('l1', 'absence:approve').dataset['state']).toBe('direct');
    expect(cell('l1', 'absence:create-own').dataset['state']).toBe('none');
    expect(cell('h1', 'absence:approve').dataset['state']).toBe('covered');
    expect(cell('h1', 'absence:approve').disabled).toBe(true);
    expect(cell('h1', 'absence:approve').title).toContain('Sammelrecht absence:*');
    expect(cell(ADMINISTRATOR_ROLE_ID, 'certificate:review').dataset['state']).toBe('covered');
  });

  it('keeps the administrator role fixed', async () => {
    await create();

    expect(cell(ADMINISTRATOR_ROLE_ID, '*').disabled).toBe(true);
    expect(q(`th[data-role="${ADMINISTRATOR_ROLE_ID}"]`)!.textContent).toContain('fest');
  });

  it('only changes the draft with a click, and shows the number of changes', async () => {
    await create();

    await clickCell('l1', 'certificate:review');

    expect(api['updateRole']).not.toHaveBeenCalled();
    expect(cell('l1', 'certificate:review').checked).toBe(true);
    expect(q('[data-testid="change-count"]')!.textContent).toContain('1 Änderung in 1 Rolle');
    expect(cell('l1', 'certificate:review').closest('td')!.classList.contains('bg-primary-container')).toBe(true);
  });

  it('takes a privilege away and puts it back without a change', async () => {
    await create();

    await clickCell('l1', 'absence:approve');
    expect(q('[data-testid="change-count"]')!.textContent).toContain('1 Änderung');
    await clickCell('l1', 'absence:approve');

    expect(q('[data-testid="change-bar"]')).toBeNull();
  });

  it('lets a collective privilege of an area be given, which then covers the single ones', async () => {
    await create();

    await clickCell('l1', 'certificate:*');

    expect(cell('l1', 'certificate:review').dataset['state']).toBe('covered');
    expect(cell('l1', 'certificate:review').disabled).toBe(true);
  });

  it('asks before saving, lists what changes, and then saves one call per changed role', async () => {
    await create();
    await clickCell('l1', 'certificate:review');
    await clickCell('p1', 'absence:create-own');

    await click('[data-action="ask-save"]');
    expect(api['updateRole']).not.toHaveBeenCalled();
    expect(q('[data-testid="confirm"]')!.textContent).toContain('+ certificate:review');
    expect(q('[data-testid="confirm"]')!.textContent).toContain('- absence:create-own');
    await click('[data-action="save"]');

    expect(api['updateRole']).toHaveBeenCalledTimes(2);
    expect(api['updateRole']).toHaveBeenCalledWith('l1', ['absence:approve', 'certificate:review']);
    expect(api['updateRole']).toHaveBeenCalledWith('p1', []);
    expect(q('[data-testid="status"]')!.textContent).toContain('2 Rollen wurden gespeichert');
    expect(api['roles']).toHaveBeenCalledTimes(2); // loaded again
  });

  it('can go back from the question and discard the changes', async () => {
    await create();
    await clickCell('l1', 'certificate:review');

    await click('[data-action="ask-save"]');
    await click('[data-action="cancel-save"]');
    expect(q('[data-testid="confirm"]')).toBeNull();
    await click('[data-action="discard"]');

    expect(q('[data-testid="change-bar"]')).toBeNull();
    expect(cell('l1', 'certificate:review').checked).toBe(false);
    expect(api['updateRole']).not.toHaveBeenCalled();
  });

  it('stops at the first refusal, names the role, and keeps the rest as a draft', async () => {
    api['updateRole'].mockResolvedValueOnce(undefined).mockRejectedValueOnce(problem('NoEscalation'));
    await create();
    await clickCell('l1', 'certificate:review');
    await clickCell('p1', 'certificate:review');
    await click('[data-action="ask-save"]');

    await click('[data-action="save"]');

    expect(api['updateRole']).toHaveBeenCalledTimes(2);
    const error = q('[data-testid="error"]')!.textContent!;
    expect(error).toContain('Filialleitung'); // the roles are saved in the order of the columns: Public User worked, Filialleitung was refused
    expect(error).toContain('nicht mehr Rechte');
    expect(error).toContain('1 Rolle(n) vorher gespeichert');
    expect(cell('l1', 'certificate:review').checked).toBe(true); // the refused change stays as a draft
  });

  it('says so if a refused call has no right behind it', async () => {
    api['updateRole'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();
    await clickCell('l1', 'certificate:review');
    await click('[data-action="ask-save"]');

    await click('[data-action="save"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('fehlt dir das Recht');
  });

  it('only shows, with a note, without the privilege to change roles', async () => {
    privileges.set(grant('access:role:read', 'access:catalog:read'));
    await create();

    expect(q('[data-testid="read-only"]')).not.toBeNull();
    expect(cell('l1', 'absence:approve').disabled).toBe(true);
    expect(cell('l1', 'certificate:review').disabled).toBe(true);
    expect(q('th[data-role="l1"]')!.textContent).toContain('nur lesen');
  });

  it('filters the rows with the search', async () => {
    await create();

    const input = q<HTMLInputElement>('[data-testid="matrix-search"]')!;
    input.value = 'AU prüfen';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();

    expect([...root.querySelectorAll('tbody tr')].map((e) => e.getAttribute('data-row'))).toEqual(['*', 'certificate:*', 'certificate:review']);
    input.value = 'gibt es nicht';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    expect(q('[data-testid="no-match"]')).not.toBeNull();
  });

  it('says so when the roles or the catalogue cannot be loaded', async () => {
    api['catalog'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('konnten nicht geladen werden');
    expect(q('[data-testid="matrix"]')).toBeNull();
  });
});
