import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OverlayContainer } from '@angular/cdk/overlay';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { StoreGroup, StoreOption } from './admin-access-models';
import { ScopesSection } from './scopes-section';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const ALL = ['access:scope:read', 'access:scope:create', 'access:scope:update', 'access:scope:delete'];

const stores: StoreOption[] = [
  { id: 's1', name: 'Augsburg Mitte', storeCode: '101', archived: false },
  { id: 's2', name: 'München Hbf', storeCode: '202', archived: false },
  { id: 's3', name: 'Ulm', storeCode: '303', archived: false },
];
const bezirk: StoreGroup = { id: 'g1', name: 'Bezirk Süd', storeIds: ['s1', 'gone'] };

describe('ScopesSection', () => {
  let api: Record<'scopes' | 'stores' | 'createScope' | 'updateScope' | 'deleteScope', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant(...ALL));
  let fixture: ComponentFixture<ScopesSection>;
  let root: HTMLElement;
  let overlay: OverlayContainer;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    overlay = TestBed.inject(OverlayContainer);
    fixture = TestBed.createComponent(ScopesSection);
    root = fixture.nativeElement as HTMLElement;
    document.body.appendChild(root);
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

  async function type(selector: string, value: string): Promise<void> {
    const input = q<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function pickStore(inputId: string, id: string): Promise<void> {
    const field = q<HTMLInputElement>(`#${inputId}`)!;
    field.focus();
    field.dispatchEvent(new Event('focusin', { bubbles: true }));
    field.dispatchEvent(new Event('focus'));
    await settle();
    overlay.getContainerElement().querySelector<HTMLElement>(`mat-option[data-id="${id}"]`)!.click();
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant(...ALL));
    api = {
      scopes: vi.fn().mockResolvedValue([bezirk]),
      stores: vi.fn().mockResolvedValue(stores),
      createScope: vi.fn().mockResolvedValue({ id: 'new' }),
      updateScope: vi.fn().mockResolvedValue(undefined),
      deleteScope: vi.fn().mockResolvedValue(undefined),
    };
  });

  afterEach(() => root?.remove());

  it('lists the groups with the names of their stores', async () => {
    await create();

    const r = q('[data-testid="scope-row"]')!;
    expect(r.textContent).toContain('Bezirk Süd');
    expect(r.textContent).toContain('2 Filialen');
    expect(r.textContent).toContain('Augsburg Mitte');
  });

  it('adds a store to a group and sends all stores, also those that are not in the list of the portal', async () => {
    await create();
    await click('[data-action="edit"]');
    expect(q('[data-testid="kept-note"]')).not.toBeNull();
    expect(q<HTMLButtonElement>('[data-action="save"]')!.disabled).toBe(true);

    await pickStore('scope-stores-g1', 's2');
    await click('[data-action="save"]');

    expect(api['updateScope']).toHaveBeenCalledWith('g1', ['s1', 's2', 'gone']);
    expect(q('[data-testid="status"]')!.textContent).toContain('gespeichert');
  });

  it('removes a store from a group', async () => {
    await create();
    await click('[data-action="edit"]');

    await click('[data-action="remove"][data-id="s1"]');
    await click('[data-action="save"]');

    expect(api['updateScope']).toHaveBeenCalledWith('g1', ['gone']);
  });

  it('explains a store outside the reach of the person', async () => {
    api['updateScope'].mockRejectedValue(new HttpErrorResponse({ status: 403, error: { title: 'NotInReach' } }));
    await create();
    await click('[data-action="edit"]');
    await pickStore('scope-stores-g1', 's3');

    await click('[data-action="save"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('Reichweite');
    expect(q('[data-testid="editor"]')).not.toBeNull();
  });

  it('creates a group with a name and stores', async () => {
    await create();
    await click('[data-action="open-create"]');

    await type('[data-testid="new-name"]', ' Bezirk Nord ');
    await pickStore('scope-new-stores', 's3');
    await click('[data-action="create"]');

    expect(api['createScope']).toHaveBeenCalledWith('Bezirk Nord', ['s3']);
    expect(q('[data-testid="status"]')!.textContent).toContain('angelegt');
  });

  it('does not create a group without a name or with an existing name', async () => {
    await create();
    await click('[data-action="open-create"]');

    await click('[data-action="create"]');
    expect(q('[data-testid="name-error"]')!.textContent).toContain('Namen');

    await type('[data-testid="new-name"]', 'bezirk süd');
    await click('[data-action="create"]');
    expect(q('[data-testid="name-error"]')!.textContent).toContain('schon');
    expect(api['createScope']).not.toHaveBeenCalled();
  });

  it('deletes only after a confirmation and says when the group is still in use', async () => {
    api['deleteScope'].mockRejectedValue(new HttpErrorResponse({ status: 409, error: { title: 'InUse' } }));
    await create();

    await click('[data-action="ask-delete"]');
    expect(api['deleteScope']).not.toHaveBeenCalled();
    await click('[data-action="cancel-delete"]');
    expect(q('[data-testid="delete-panel"]')).toBeNull();

    await click('[data-action="ask-delete"]');
    await click('[data-action="delete"]');

    expect(api['deleteScope']).toHaveBeenCalledWith('g1');
    expect(q('[data-testid="error"]')!.textContent).toContain('verwendet');
  });

  it('hides create, change and delete without their privileges', async () => {
    privileges.set(grant('access:scope:read'));
    await create();

    expect(q('[data-action="open-create"]')).toBeNull();
    expect(q('[data-action="ask-delete"]')).toBeNull();
    await click('[data-action="edit"]');
    expect(q('[data-action="save"]')).toBeNull();
    expect(q('app-store-picker')).toBeNull();
  });

  it('says so when the stores cannot be loaded', async () => {
    api['stores'].mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    await create();

    expect(q('[data-testid="stores-error"]')).not.toBeNull();
  });
});
