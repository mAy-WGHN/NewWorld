import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { MainStore, StoreOption } from './admin-access-models';
import { MainStorePanel } from './main-store-panel';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const ALL = ['access:person:read', 'access:main-store:update'];

const PERSON = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const stores: StoreOption[] = [
  { id: 's1', name: 'Augsburg Mitte', storeCode: '101', archived: false },
  { id: 's2', name: 'München Hbf', storeCode: '202', archived: false },
];

describe('MainStorePanel', () => {
  let api: Record<'mainStore' | 'stores' | 'setMainStore' | 'clearMainStore', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant(...ALL));
  let fixture: ComponentFixture<MainStorePanel>;
  let root: HTMLElement;

  const state = (over: Partial<MainStore> = {}): MainStore => ({ effectiveStoreId: 's1', overrideStoreId: null, suggestedStoreId: 's1', ...over });

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(MainStorePanel);
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

  async function click(selector: string): Promise<void> {
    q<HTMLElement>(selector)!.click();
    await settle();
  }

  async function choose(id: string): Promise<void> {
    const select = q<HTMLSelectElement>('[data-testid="store-select"]')!;
    select.value = id;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    await settle();
  }

  beforeEach(() => {
    privileges.set(grant(...ALL));
    api = {
      mainStore: vi.fn().mockResolvedValue(state()),
      stores: vi.fn().mockResolvedValue(stores),
      setMainStore: vi.fn().mockResolvedValue(undefined),
      clearMainStore: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('shows the effective store, the manual one and the BC suggestion by name', async () => {
    api['mainStore'].mockResolvedValue(state({ effectiveStoreId: 's2', overrideStoreId: 's2', suggestedStoreId: 's1' }));
    await create();

    expect(q('[data-testid="effective"]')!.textContent).toContain('München Hbf (202)');
    expect(q('[data-testid="override"]')!.textContent).toContain('München Hbf (202)');
    expect(q('[data-testid="suggested"]')!.textContent).toContain('Augsburg Mitte (101)');
  });

  it('sets the main store and shows the new state', async () => {
    api['mainStore'].mockResolvedValueOnce(state()).mockResolvedValueOnce(state({ effectiveStoreId: 's2', overrideStoreId: 's2' }));
    await create();

    await choose('s2');
    await click('[data-action="save"]');

    expect(api['setMainStore']).toHaveBeenCalledWith(PERSON, 's2');
    expect(q('[data-testid="effective"]')!.textContent).toContain('München Hbf');
    expect(q('[data-testid="status"]')!.textContent).toContain('gesetzt');
  });

  it('does not call the service without a chosen store', async () => {
    await create();

    await click('[data-action="save"]');

    expect(q('[data-testid="form-error"]')!.textContent).toContain('Filiale');
    expect(api['setMainStore']).not.toHaveBeenCalled();
  });

  it('explains when the store or the person is out of reach', async () => {
    api['setMainStore'].mockRejectedValue(new HttpErrorResponse({ status: 403, error: { title: 'NotInReach' } }));
    await create();
    await choose('s2');

    await click('[data-action="save"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('Reichweite');
  });

  it('removes the manual setting only after a confirmation, and only when there is one', async () => {
    await create();
    expect(q('[data-action="ask-clear"]')).toBeNull();
    fixture.destroy();
    TestBed.resetTestingModule();

    api['mainStore'].mockResolvedValue(state({ overrideStoreId: 's2', effectiveStoreId: 's2' }));
    await create();
    await click('[data-action="ask-clear"]');
    expect(api['clearMainStore']).not.toHaveBeenCalled();
    await click('[data-action="cancel-clear"]');
    expect(api['clearMainStore']).not.toHaveBeenCalled();

    await click('[data-action="ask-clear"]');
    await click('[data-action="clear"]');

    expect(api['clearMainStore']).toHaveBeenCalledWith(PERSON);
    expect(q('[data-testid="status"]')!.textContent).toContain('Vorschlag aus BC');
  });

  it('says that an unknown person is not in Access', async () => {
    api['mainStore'].mockRejectedValue(new HttpErrorResponse({ status: 404 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('nicht bekannt');
  });

  it('shows no state without the privilege to read, but allows changing', async () => {
    privileges.set(grant('access:main-store:update'));
    await create();

    expect(api['mainStore']).not.toHaveBeenCalled();
    expect(q('[data-testid="no-read"]')).not.toBeNull();
    expect(q('[data-testid="set-panel"]')).not.toBeNull();
  });

  it('hides changing without the privilege', async () => {
    privileges.set(grant('access:person:read'));
    await create();

    expect(q('[data-testid="set-panel"]')).toBeNull();
    expect(q('[data-testid="main-store-state"]')).not.toBeNull();
  });
});
