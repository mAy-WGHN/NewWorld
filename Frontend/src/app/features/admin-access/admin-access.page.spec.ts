import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { AdminAccessPage } from './admin-access.page';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

const PERSON = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';

describe('AdminAccessPage', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  const privileges = signal(grant());
  let fixture: ComponentFixture<AdminAccessPage>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(AdminAccessPage);
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
  const tabs = () => [...root.querySelectorAll('[data-testid="tab"]')].map((e) => e.getAttribute('data-key'));

  async function set(selector: string, value: string): Promise<void> {
    const el = q<HTMLInputElement>(selector)!;
    el.value = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant());
    api = {
      roles: vi.fn().mockResolvedValue([]),
      catalog: vi.fn().mockResolvedValue([]),
      scopes: vi.fn().mockResolvedValue([]),
      stores: vi.fn().mockResolvedValue([]),
      assignments: vi.fn().mockResolvedValue([]),
      responsibilities: vi.fn().mockResolvedValue([]),
      searchPeople: vi.fn().mockResolvedValue([{ employeeId: PERSON, personnelNumber: '10042', name: 'Erika Mustermann', homeStoreId: null, position: null, exitDate: null }]),
      peopleByIds: vi.fn().mockResolvedValue([{ employeeId: PERSON, personnelNumber: '10042', name: 'Erika Mustermann', homeStoreId: null, position: null, exitDate: null }]),
      mainStore: vi.fn().mockResolvedValue({ effectiveStoreId: null, overrideStoreId: null, suggestedStoreId: null }),
    };
  });

  it('is titled "Rollen und Rechte"', async () => {
    privileges.set(grant('access:role:read'));
    await create();

    expect(q('h1')!.textContent).toContain('Rollen und Rechte');
  });

  it('shows a tab only for what the person may read', async () => {
    privileges.set(grant('access:role:read', 'access:scope:read'));
    await create();

    expect(tabs()).toEqual(['roles', 'matrix', 'scopes']);
    expect(q('app-roles-section')).not.toBeNull();
    expect(api['catalog']).toHaveBeenCalled(); // the roles section loads the catalogue for the privilege choice
  });

  it('shows all five tabs with all read privileges, and the person tab for any of its privileges', async () => {
    privileges.set(grant('access:role:read', 'access:catalog:read', 'access:scope:read', 'access:assignment:read'));
    await create();
    expect(tabs()).toEqual(['roles', 'matrix', 'catalog', 'scopes', 'people']);
    fixture.destroy();
    TestBed.resetTestingModule();

    privileges.set(grant('access:main-store:update'));
    await create();
    expect(tabs()).toEqual(['people']);
  });

  it('shows nothing but a note without any of the privileges', async () => {
    privileges.set(grant('absence:create-own'));
    await create();

    expect(tabs()).toEqual([]);
    expect(q('[data-testid="nothing"]')).not.toBeNull();
    expect(q('[data-testid="panel"]')).toBeNull();
  });

  it('starts with the first tab the person may see and switches on click', async () => {
    privileges.set(grant('access:catalog:read', 'access:scope:read'));
    await create();
    expect(q('[data-testid="panel"] app-catalog-section')).not.toBeNull();
    expect(q('[data-key="catalog"]')!.getAttribute('aria-selected')).toBe('true');

    q<HTMLButtonElement>('[data-key="scopes"]')!.click();
    await settle();

    expect(q('[data-testid="panel"] app-scopes-section')).not.toBeNull();
    expect(q('[data-testid="panel"] app-catalog-section')).toBeNull();
    expect(q('[data-key="scopes"]')!.getAttribute('aria-selected')).toBe('true');
  });

  it('moves between the tabs with the arrow keys', async () => {
    privileges.set(grant('access:catalog:read', 'access:scope:read'));
    await create();

    q('[role="tablist"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await settle();

    expect(q('[data-key="scopes"]')!.getAttribute('aria-selected')).toBe('true');
  });

  it('finds a person by name before it loads anything, and shows the panels the privileges allow', async () => {
    privileges.set(grant('access:assignment:read', 'access:main-store:update'));
    await create();
    expect(q('[data-testid="assignments-panel"]')).toBeNull();
    expect(q('[data-testid="person-id"]')).toBeNull(); // no field for ids any more

    await set('[data-testid="person-search"]', 'muster');
    await new Promise((r) => setTimeout(r, 400));
    await settle();
    expect(api['searchPeople']).toHaveBeenCalledWith('muster');
    expect(api['assignments']).not.toHaveBeenCalled(); // nothing is loaded before a person is chosen
    q<HTMLButtonElement>('[data-testid="person-result"]')!.click();
    await settle();

    expect(api['assignments']).toHaveBeenCalledWith(PERSON);
    expect(q('[data-testid="person-selected"]')!.textContent).toContain('Erika Mustermann');
    expect(q('[data-testid="assignments-panel"]')).not.toBeNull();
    expect(q('[data-testid="main-store-panel"]')).not.toBeNull();
    expect(q('[data-testid="responsibilities-panel"]')).toBeNull();
  });
});
