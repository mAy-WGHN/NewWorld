import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { RoleDefinitionFile, RoleImportResult, importErrorText, parseRoleFile } from './admin-access-models';
import { RoleTransfer } from './role-transfer';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

const FILE: RoleDefinitionFile = { version: 1, customPrivileges: [], roles: [{ name: 'Filialleitung', privileges: ['absence:read'] }, { name: 'Neu', privileges: ['absence:read'] }] };
const COMPARISON: RoleImportResult = {
  applied: false,
  hasErrors: false,
  items: [
    { kind: 'role', name: 'Filialleitung', action: 'update', added: ['absence:read'], removed: ['absence:approve'], error: null },
    { kind: 'role', name: 'Neu', action: 'create', added: ['absence:read'], removed: [], error: null },
    { kind: 'role', name: 'Public User', action: 'unchanged', added: [], removed: [], error: null },
  ],
};

describe('RoleTransfer', () => {
  let api: Record<'roleDefinitions' | 'importRoles', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant('access:role:read', 'access:role:update'));
  let fixture: ComponentFixture<RoleTransfer>;
  let root: HTMLElement;
  let imported: number;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(RoleTransfer);
    fixture.componentInstance.imported.subscribe(() => imported++);
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

  async function pick(content: string): Promise<void> {
    const input = q<HTMLInputElement>('[data-testid="file-input"]')!;
    const file = new File([content], 'rollen.json', { type: 'application/json' });
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle();
  }

  beforeEach(() => {
    imported = 0;
    privileges.set(grant('access:role:read', 'access:role:update'));
    api = { roleDefinitions: vi.fn().mockResolvedValue(FILE), importRoles: vi.fn().mockResolvedValue(COMPARISON) };
    URL.createObjectURL = vi.fn(() => 'blob:rollen');
    URL.revokeObjectURL = vi.fn();
  });

  it('offers export and comparison only with the matching rights', async () => {
    privileges.set(grant('access:role:read'));
    await create();

    expect(q('[data-action="export"]')).not.toBeNull();
    expect(q('[data-action="pick"]')).toBeNull();
  });

  it('shows nothing to do without any right', async () => {
    privileges.set(grant());
    await create();

    expect(q('[data-action="export"]')).toBeNull();
    expect(q('[data-action="pick"]')).toBeNull();
  });

  it('exports the roles as a file named rollen.json', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await create();

    q<HTMLButtonElement>('[data-action="export"]')!.click();
    await settle();

    expect(api['roleDefinitions']).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    expect(q('[data-testid="status"]')!.textContent).toContain('rollen.json');
    click.mockRestore();
  });

  it('compares a file first and changes nothing', async () => {
    await create();

    await pick(JSON.stringify(FILE));

    expect(api['importRoles']).toHaveBeenCalledWith(FILE, false, []);
    const items = [...root.querySelectorAll('[data-testid="diff-item"]')].map((e) => e.getAttribute('data-name'));
    expect(items).toEqual(['Filialleitung', 'Neu']);
    expect(q('[data-testid="diff-added"]')!.textContent).toContain('+ absence:read');
    expect(q('[data-testid="diff-removed"]')!.textContent).toContain('absence:approve');
    expect(api['importRoles']).not.toHaveBeenCalledWith(expect.anything(), true, expect.anything());
  });

  it('applies only the ticked items', async () => {
    api['importRoles'].mockResolvedValueOnce(COMPARISON).mockResolvedValueOnce({ ...COMPARISON, applied: true });
    await create();
    await pick(JSON.stringify(FILE));

    q<HTMLElement>('[data-name="Filialleitung"] input[type="checkbox"]')!.click();
    await settle();
    q<HTMLButtonElement>('[data-action="apply"]')!.click();
    await settle();

    expect(api['importRoles']).toHaveBeenLastCalledWith(FILE, true, ['Neu']);
    expect(imported).toBe(1);
    expect(q('[data-testid="status"]')!.textContent).toContain('1 Änderung');
    expect(q('[data-testid="diff"]')).toBeNull();
  });

  it('has nothing to apply when the file is already the same', async () => {
    api['importRoles'].mockResolvedValue({ applied: false, hasErrors: false, items: [{ kind: 'role', name: 'A', action: 'unchanged', added: [], removed: [], error: null }] });
    await create();

    await pick(JSON.stringify(FILE));

    expect(q('[data-testid="diff-empty"]')).not.toBeNull();
    expect(q('[data-action="apply"]')).toBeNull();
  });

  it('shows errors of the file and offers no apply button', async () => {
    api['importRoles'].mockResolvedValue({
      applied: false,
      hasErrors: true,
      items: [{ kind: 'role', name: 'Schlecht', action: 'error', added: [], removed: [], error: 'UnknownPrivilege: gibt:es-nicht' }, ...COMPARISON.items.slice(0, 1)],
    });
    await create();

    await pick(JSON.stringify(FILE));

    expect(q('[data-testid="diff-error"]')!.textContent).toContain('gibt:es-nicht');
    expect(q('[data-action="apply"]')).toBeNull();
  });

  it('says so if the file is no role file, without calling the service', async () => {
    await create();

    await pick('das ist kein JSON');

    expect(q('[data-testid="file-error"]')!.textContent).toContain('JSON');
    expect(api['importRoles']).not.toHaveBeenCalled();
  });

  it('discards a comparison', async () => {
    await create();
    await pick(JSON.stringify(FILE));

    q<HTMLButtonElement>('[data-action="cancel-import"]')!.click();
    await settle();

    expect(q('[data-testid="diff"]')).toBeNull();
  });
});

describe('role file helpers', () => {
  it('reads a valid file', () => {
    expect(parseRoleFile(JSON.stringify(FILE))).toEqual({ file: FILE });
  });

  it('adds missing own privileges as an empty list', () => {
    const parsed = parseRoleFile('{"version":1,"roles":[]}');

    expect(parsed).toEqual({ file: { version: 1, customPrivileges: [], roles: [] } });
  });

  it('refuses text, other JSON and roles without name or rights', () => {
    for (const text of ['x', '[]', 'null', '{"roles":[]}', '{"version":1,"roles":[{"name":1,"privileges":[]}]}', '{"version":1,"roles":[{"name":"A","privileges":[1]}]}']) {
      expect('error' in parseRoleFile(text)).toBe(true);
    }
  });

  it('explains the error codes of Access', () => {
    expect(importErrorText('UnknownPrivilege: x:y')).toContain('x:y');
    expect(importErrorText('BuiltInRole')).toContain('Administrator');
    expect(importErrorText('Duplicate')).toContain('mehrfach');
    expect(importErrorText(null)).toBe('Unbekannter Fehler.');
  });
});
