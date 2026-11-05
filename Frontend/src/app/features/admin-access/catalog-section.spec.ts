import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { CatalogEntry } from './admin-access-models';
import { CatalogSection } from './catalog-section';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

const entries: CatalogEntry[] = [
  { key: 'absence:approve', owner: 'workforce', scopable: true, description: 'Urlaub genehmigen.' },
  { key: 'access:role:read', owner: 'access', scopable: false, description: 'Rollen ansehen.' },
  { key: 'inventur:freigeben', owner: 'custom', scopable: false, description: '' },
];

describe('CatalogSection', () => {
  let api: Record<'catalog' | 'createCustomPrivilege' | 'deleteCustomPrivilege', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant('access:catalog:read', 'access:custom-privilege:create', 'access:custom-privilege:delete'));
  let fixture: ComponentFixture<CatalogSection>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(CatalogSection);
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
  const row = (key: string) => q(`[data-testid="privilege-row"][data-key="${key}"]`)!;

  async function click(selector: string, scope: ParentNode = root): Promise<void> {
    scope.querySelector<HTMLElement>(selector)!.click();
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const input = q<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant('access:catalog:read', 'access:custom-privilege:create', 'access:custom-privilege:delete'));
    api = {
      catalog: vi.fn().mockResolvedValue(entries),
      createCustomPrivilege: vi.fn().mockResolvedValue(undefined),
      deleteCustomPrivilege: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('lists all privileges per application with description and the scopable flag, own ones last', async () => {
    await create();

    expect([...root.querySelectorAll('[data-owner]')].map((e) => e.getAttribute('data-owner'))).toEqual(['access', 'workforce', 'custom']);
    expect(row('absence:approve').textContent).toContain('Urlaub genehmigen.');
    expect(row('absence:approve').querySelector('[data-testid="scopable-chip"]')).not.toBeNull();
    expect(row('access:role:read').querySelector('[data-testid="scopable-chip"]')).toBeNull();
    expect(row('inventur:freigeben').textContent).toContain('Keine Beschreibung.');
  });

  it('filters by the search', async () => {
    await create();

    await type('[data-testid="catalog-search"]', 'rollen');
    expect([...root.querySelectorAll('[data-testid="privilege-row"]')].map((e) => e.getAttribute('data-key'))).toEqual(['access:role:read']);

    await type('[data-testid="catalog-search"]', 'nichts');
    expect(q('[data-testid="empty"]')).not.toBeNull();
  });

  it('allows deleting only own privileges', async () => {
    await create();

    expect(row('absence:approve').querySelector('[data-action="ask-delete"]')).toBeNull();
    expect(row('access:role:read').querySelector('[data-action="ask-delete"]')).toBeNull();
    expect(row('inventur:freigeben').querySelector('[data-action="ask-delete"]')).not.toBeNull();
  });

  it('creates an own privilege with description and scopable flag', async () => {
    await create();
    await click('[data-action="open-create"]');

    await type('[data-testid="new-key"]', ' inventur:zaehlen ');
    await type('[data-testid="new-description"]', 'Bestand zählen.');
    q<HTMLInputElement>('[data-testid="new-scopable"] input')!.click();
    await settle();
    await click('[data-action="create"]');

    expect(api['createCustomPrivilege']).toHaveBeenCalledWith('inventur:zaehlen', true, 'Bestand zählen.');
    expect(api['catalog']).toHaveBeenCalledTimes(2);
    expect(q('[data-testid="status"]')!.textContent).toContain('angelegt');
  });

  it('sends no description when it is empty', async () => {
    await create();
    await click('[data-action="open-create"]');
    await type('[data-testid="new-key"]', 'inventur:zaehlen');

    await click('[data-action="create"]');

    expect(api['createCustomPrivilege']).toHaveBeenCalledWith('inventur:zaehlen', false, null);
  });

  it('refuses wrong keys and keys that exist before calling the service', async () => {
    await create();
    await click('[data-action="open-create"]');

    await type('[data-testid="new-key"]', 'inventur:*');
    await click('[data-action="create"]');
    expect(q('[data-testid="key-error"]')!.textContent).toContain('bereich:aktion');

    await type('[data-testid="new-key"]', 'absence:approve');
    await click('[data-action="create"]');
    expect(q('[data-testid="key-error"]')!.textContent).toContain('schon');
    expect(api['createCustomPrivilege']).not.toHaveBeenCalled();
  });

  it('shows the message of the service when creating fails', async () => {
    api['createCustomPrivilege'].mockRejectedValue(new HttpErrorResponse({ status: 422, error: { title: 'Invalid' } }));
    await create();
    await click('[data-action="open-create"]');
    await type('[data-testid="new-key"]', 'inventur:zaehlen');

    await click('[data-action="create"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('nicht gültig');
  });

  it('deletes an own privilege only after a confirmation', async () => {
    await create();
    await click('[data-action="ask-delete"]', row('inventur:freigeben'));
    expect(api['deleteCustomPrivilege']).not.toHaveBeenCalled();

    await click('[data-action="cancel-delete"]');
    expect(api['deleteCustomPrivilege']).not.toHaveBeenCalled();

    await click('[data-action="ask-delete"]', row('inventur:freigeben'));
    await click('[data-action="delete"]');

    expect(api['deleteCustomPrivilege']).toHaveBeenCalledWith('inventur:freigeben');
    expect(q('[data-testid="status"]')!.textContent).toContain('gelöscht');
  });

  it('is read-only without the privileges to create and delete', async () => {
    privileges.set(grant('access:catalog:read'));
    await create();

    expect(q('[data-action="open-create"]')).toBeNull();
    expect(row('inventur:freigeben').querySelector('[data-action="ask-delete"]')).toBeNull();
  });

  it('says so when the catalogue cannot be loaded', async () => {
    api['catalog'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('nicht geladen');
  });
});
