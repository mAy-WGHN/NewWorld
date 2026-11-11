import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from './admin-access.api';
import { Person, ResponsibilityEntry, StoreGroup, StoreOption, dayAfter } from './admin-access-models';
import { ResponsibilitiesPanel } from './responsibilities-panel';

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
const ALL = ['access:responsibility:read', 'access:responsibility:create', 'access:responsibility:delete'];

const PERSON = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const BOSS = '9b2c1f64-1111-4222-8333-444455556666';
const person = (employeeId: string, name: string, personnelNumber: string): Person => ({ employeeId, name, personnelNumber, homeStoreId: null, position: null, exitDate: null });
const ERIKA = person(PERSON, 'Erika Mustermann', '10042');
const MAX = person(BOSS, 'Max Chef', '10001');
const stores: StoreOption[] = [{ id: 's1', name: 'Augsburg Mitte', storeCode: '101', archived: false }];
const scopes: StoreGroup[] = [{ id: 'g1', name: 'Bezirk Süd', storeIds: ['s1'] }];
const ENTRY_ID = 'b2c1f64a-1111-4222-8333-444455556666';
const ENTRY: ResponsibilityEntry = { id: ENTRY_ID, topic: 'absence.approval', responsibleEmployeeId: BOSS, target: { kind: 'store', id: 's1' }, validFrom: null, validTo: null };

describe('ResponsibilitiesPanel', () => {
  let api: Record<'responsibilities' | 'allResponsibilities' | 'stores' | 'scopes' | 'addResponsibility' | 'removeResponsibility' | 'searchPeople' | 'peopleByIds', ReturnType<typeof vi.fn>>;
  const privileges = signal(grant(...ALL));
  let fixture: ComponentFixture<ResponsibilitiesPanel>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }, { provide: AuthService, useValue: { privileges } }] });
    fixture = TestBed.createComponent(ResponsibilitiesPanel);
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

  /** Types into the person search of the field and takes the first hit (the search waits a moment after typing). */
  async function pickPerson(field: string, text: string, found: Person): Promise<void> {
    api['searchPeople'].mockResolvedValue([found]);
    await set(`[data-testid="${field}"] [data-testid="person-search"]`, text);
    await new Promise((r) => setTimeout(r, 400));
    await settle();
    await click(`[data-testid="${field}"] [data-testid="person-result"]`);
  }

  beforeEach(() => {
    privileges.set(grant(...ALL));
    api = {
      searchPeople: vi.fn().mockResolvedValue([]),
      peopleByIds: vi.fn().mockImplementation(async (ids: string[]) => [ERIKA, MAX].filter((p) => ids.includes(p.employeeId))),
      responsibilities: vi.fn().mockResolvedValue([{ topic: 'absence.approval', responsibleEmployeeIds: [BOSS] }]),
      allResponsibilities: vi.fn().mockResolvedValue([ENTRY]),
      stores: vi.fn().mockResolvedValue(stores),
      scopes: vi.fn().mockResolvedValue(scopes),
      addResponsibility: vi.fn().mockResolvedValue({ id: 'b2c1f64a-1111-4222-8333-444455556666' }),
      removeResponsibility: vi.fn().mockResolvedValue(undefined),
    };
  });

  it('lists who is responsible for the person per topic, with a plain name for known topics', async () => {
    await create();

    const r = q('[data-testid="topic-row"]')!;
    expect(r.textContent).toContain('Urlaub genehmigen');
    expect(r.textContent).toContain('absence.approval');
    expect(r.querySelector('[data-testid="responsible"]')!.textContent).toContain('Max Chef (10001)'); // the name, not the id
  });

  it('offers the known topics as suggestions', async () => {
    await create();

    const topics = [...root.querySelectorAll('#topic-suggestions option')].map((o) => (o as HTMLOptionElement).value);
    expect(topics).toEqual(['absence.approval', 'schedule.adjustment', 'employee.change-request', 'workingtime.payout']);
  });

  it('says so when nobody is responsible and when the list is out of reach', async () => {
    api['responsibilities'].mockResolvedValue([]);
    await create();
    expect(q('[data-testid="no-items"]')).not.toBeNull();
  });

  it('says when the person is out of reach', async () => {
    api['responsibilities'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('nicht sehen');
  });

  it('creates a responsibility for the loaded person, with the end day inclusive', async () => {
    await create();

    expect(q('[data-testid="target-employee"] [data-testid="person-selected"]')!.textContent).toContain('Erika Mustermann');
    await set('[data-testid="topic"]', ' absence.approval ');
    await pickPerson('responsible-id', 'max', MAX);
    await set('[data-testid="to"]', '2026-12-31');
    await click('[data-action="add"]');

    expect(api['addResponsibility']).toHaveBeenCalledWith({
      topic: 'absence.approval',
      responsibleEmployeeId: BOSS,
      target: { kind: 'employee', id: PERSON },
      validFrom: null,
      validTo: dayAfter('2026-12-31'),
    });
    expect(q('[data-testid="status"]')!.textContent).toContain('eingerichtet');
    expect(api['allResponsibilities']).toHaveBeenCalledTimes(2); // the full list is loaded again after the entry
  });

  it('lists all responsibilities in reach with names, target and period, and says so when there are none', async () => {
    api['allResponsibilities'].mockResolvedValue([
      ENTRY,
      { ...ENTRY, id: 'c3c1f64a-1111-4222-8333-444455556666', topic: 'schedule.adjustment', target: { kind: 'everyone' }, validTo: dayAfter('2026-12-31') },
    ]);
    await create();

    const rows = [...root.querySelectorAll('[data-testid="entry-row"]')];
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Urlaub genehmigen');
    expect(rows[0].textContent).toContain('Max Chef (10001)');
    expect(rows[0].textContent).toContain('Augsburg Mitte (101)'); // the store by name
    expect(rows[0].querySelector('[data-testid="entry-period"]')!.textContent).toContain('unbefristet');
    expect(rows[1].textContent).toContain('alle Mitarbeitenden');
    expect(rows[1].querySelector('[data-testid="entry-period"]')!.textContent).toContain('bis 31.12.2026');
  });

  it('says so when no responsibility is set up at all', async () => {
    api['allResponsibilities'].mockResolvedValue([]);
    await create();

    expect(q('[data-testid="no-entries"]')).not.toBeNull();
  });

  it('says so when the full list cannot be loaded, the rest of the panel still works', async () => {
    api['allResponsibilities'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await create();

    expect(q('[data-testid="all-error"]')).not.toBeNull();
    expect(q('[data-testid="topic-row"]')).not.toBeNull();
  });

  it('creates a responsibility for a store and for a group', async () => {
    await create();
    await set('[data-testid="topic"]', 'schedule.adjustment');
    await pickPerson('responsible-id', 'max', MAX);

    await set('[data-testid="kind"]', 'store', 'change');
    await set('[data-testid="target-store"]', 's1', 'change');
    await click('[data-action="add"]');
    expect(api['addResponsibility']).toHaveBeenLastCalledWith(expect.objectContaining({ target: { kind: 'store', id: 's1' } }));

    await set('[data-testid="topic"]', 'schedule.adjustment');
    await pickPerson('responsible-id', 'max', MAX); // the form is empty again after an entry
    await set('[data-testid="kind"]', 'scope', 'change');
    await set('[data-testid="target-scope"]', 'g1', 'change');
    await click('[data-action="add"]');
    expect(api['addResponsibility']).toHaveBeenLastCalledWith(expect.objectContaining({ target: { kind: 'scope', id: 'g1' } }));
  });

  it('sets a responsibility for everybody without a target id', async () => {
    await create();
    await set('[data-testid="topic"]', 'absence.approval');
    await pickPerson('responsible-id', 'max', MAX);
    await set('[data-testid="kind"]', 'everyone', 'change');
    expect(q('[data-testid="target-everyone"]')).not.toBeNull();
    await click('[data-action="add"]');
    expect(api['addResponsibility']).toHaveBeenLastCalledWith(expect.objectContaining({ target: { kind: 'everyone' } }));
  });

  it('checks topic, person ids, target and period before it calls the service', async () => {
    await create();

    await click('[data-action="add"]');
    expect(q('[data-testid="form-error"]')!.textContent).toContain('Thema');

    await set('[data-testid="topic"]', 'Urlaub genehmigen');
    await click('[data-action="add"]');
    expect(q('[data-testid="form-error"]')!.textContent).toContain('Thema');

    await set('[data-testid="topic"]', 'absence.approval');
    await click('[data-action="add"]');
    expect(q('[data-testid="form-error"]')!.textContent).toContain('zuständige Person auswählen');

    await pickPerson('responsible-id', 'max', MAX);
    await set('[data-testid="kind"]', 'store', 'change');
    await click('[data-action="add"]');
    expect(q('[data-testid="form-error"]')!.textContent).toContain('Ziel');

    await set('[data-testid="kind"]', 'employee', 'change');
    await set('[data-testid="from"]', '2026-12-10');
    await set('[data-testid="to"]', '2026-12-01');
    await click('[data-action="add"]');
    expect(q('[data-testid="form-error"]')!.textContent).toContain('vor');

    expect(api['addResponsibility']).not.toHaveBeenCalled();
  });

  it('explains when the target is out of reach', async () => {
    api['addResponsibility'].mockRejectedValue(new HttpErrorResponse({ status: 403, error: { title: 'NotInReach' } }));
    await create();
    await set('[data-testid="topic"]', 'absence.approval');
    await pickPerson('responsible-id', 'max', MAX);

    await click('[data-action="add"]');

    expect(q('[data-testid="error"]')!.textContent).toContain('Reichweite');
  });

  it('removes a listed responsibility only after a confirmation and shows the list again afterwards', async () => {
    await create();

    await click('[data-testid="entry-row"] [data-action="ask-delete"]');
    expect(api['removeResponsibility']).not.toHaveBeenCalled();
    await click('[data-action="cancel-delete"]');
    expect(api['removeResponsibility']).not.toHaveBeenCalled();

    api['allResponsibilities'].mockResolvedValue([]); // the service no longer lists it
    await click('[data-testid="entry-row"] [data-action="ask-delete"]');
    await click('[data-action="delete"]');

    expect(api['removeResponsibility']).toHaveBeenCalledWith(ENTRY_ID);
    expect(q('[data-testid="entry-row"]')).toBeNull();
    expect(q('[data-testid="delete-panel"]')).toBeNull();
  });

  it('hides creating and removing without the privileges', async () => {
    privileges.set(grant('access:responsibility:read'));
    await create();

    expect(q('[data-testid="add-panel"]')).toBeNull();
    expect(q('[data-testid="entry-row"]')).not.toBeNull(); // still listed
    expect(q('[data-testid="entry-row"] [data-action="ask-delete"]')).toBeNull();
  });
});
