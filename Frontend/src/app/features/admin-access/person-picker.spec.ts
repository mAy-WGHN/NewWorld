import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminAccessApi } from './admin-access.api';
import { Person } from './admin-access-models';
import { PersonPicker } from './person-picker';

const ID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';
const person = (over: Partial<Person> = {}): Person => ({
  employeeId: ID, personnelNumber: '10042', name: 'Erika Mustermann', homeStoreId: null, position: 'Verkauf', exitDate: null, ...over,
});

describe('PersonPicker', () => {
  let api: Record<'searchPeople' | 'peopleByIds', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<PersonPicker>;
  let root: HTMLElement;
  let picked: (Person | null)[];

  async function create(selectedId: string | null = null): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: AdminAccessApi, useValue: api }] });
    fixture = TestBed.createComponent(PersonPicker);
    fixture.componentRef.setInput('label', 'Person');
    fixture.componentRef.setInput('inputId', 'p');
    fixture.componentRef.setInput('debounceMs', 0);
    fixture.componentRef.setInput('selectedId', selectedId);
    picked = [];
    fixture.componentInstance.picked.subscribe((p) => picked.push(p));
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

  async function type(text: string): Promise<void> {
    const input = q<HTMLInputElement>('[data-testid="person-search"]')!;
    input.value = text;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  beforeEach(() => {
    api = { searchPeople: vi.fn().mockResolvedValue([person()]), peopleByIds: vi.fn().mockResolvedValue([person()]) };
  });

  it('searches by name or personnel number and shows the hits with name and number', async () => {
    await create();

    await type('muster');

    expect(api['searchPeople']).toHaveBeenCalledWith('muster');
    const hit = q('[data-testid="person-result"]')!;
    expect(hit.textContent).toContain('Erika Mustermann');
    expect(hit.textContent).toContain('10042');
    expect(hit.textContent).toContain('Verkauf');
  });

  it('does not search for fewer than two characters', async () => {
    await create();

    await type('e');

    expect(api['searchPeople']).not.toHaveBeenCalled();
    expect(q('[data-testid="person-result"]')).toBeNull();
    expect(q('[data-testid="person-none"]')).toBeNull();
  });

  it('waits after typing and only asks once for what was typed last', async () => {
    await create();
    fixture.componentRef.setInput('debounceMs', 400); // long enough that a slow test run does not let the first keys through

    await type('mu');
    await type('mus');
    await type('must');
    expect(api['searchPeople']).not.toHaveBeenCalled(); // still waiting
    await new Promise((r) => setTimeout(r, 600));
    await settle();

    expect(api['searchPeople']).toHaveBeenCalledTimes(1);
    expect(api['searchPeople']).toHaveBeenCalledWith('must');
  });

  it('takes the person that was clicked, shows it with name and number, and tells the page', async () => {
    await create();
    await type('muster');

    q<HTMLButtonElement>('[data-testid="person-result"]')!.click();
    await settle();

    expect(picked).toEqual([person()]);
    expect(q('[data-testid="person-selected"]')!.textContent).toContain('Erika Mustermann');
    expect(q('[data-testid="person-selected"]')!.textContent).toContain('10042');
    expect(q('[data-testid="person-search"]')).toBeNull();
  });

  it('lets the choice be changed', async () => {
    await create();
    await type('muster');
    q<HTMLButtonElement>('[data-testid="person-result"]')!.click();
    await settle();

    q<HTMLButtonElement>('[data-action="clear-person"]')!.click();
    await settle();

    expect(picked).toEqual([person(), null]);
    expect(q('[data-testid="person-search"]')).not.toBeNull();
  });

  it('takes the only hit with Enter', async () => {
    await create();
    await type('muster');

    q('[data-testid="person-search"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await settle();

    expect(picked).toEqual([person()]);
  });

  it('does not decide with Enter if there are several hits', async () => {
    api['searchPeople'].mockResolvedValue([person(), person({ employeeId: 'b', name: 'Max Muster', personnelNumber: '10001' })]);
    await create();
    await type('muster');

    q('[data-testid="person-search"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await settle();

    expect(picked).toEqual([]);
    expect(root.querySelectorAll('[data-testid="person-result"]').length).toBe(2);
  });

  it('says so when nobody is found, and that only people in reach are shown', async () => {
    api['searchPeople'].mockResolvedValue([]);
    await create();

    await type('xyz');

    expect(q('[data-testid="person-none"]')!.textContent).toContain('Keine Person gefunden');
    expect(q('[data-testid="person-none"]')!.textContent).toContain('Reichweite');
  });

  it('marks a person who has left', async () => {
    api['searchPeople'].mockResolvedValue([person({ exitDate: '2026-06-30' })]);
    await create();

    await type('muster');

    expect(q('[data-testid="person-result"]')!.textContent).toContain('ausgeschieden');
  });

  it('says in plain words when the search is refused or fails', async () => {
    api['searchPeople'].mockRejectedValue(new HttpErrorResponse({ status: 403 }));
    await create();
    await type('muster');
    expect(q('[data-testid="person-error"]')!.textContent).toContain('fehlt dir das Recht');

    api['searchPeople'].mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await type('muster2');
    expect(q('[data-testid="person-error"]')!.textContent).toContain('nicht geklappt');
    expect(q('[data-testid="person-result"]')).toBeNull();
  });

  it('resolves a pasted id and offers that person', async () => {
    await create();

    await type(ID.toUpperCase());

    expect(api['peopleByIds']).toHaveBeenCalledWith([ID]);
    expect(api['searchPeople']).not.toHaveBeenCalled();
    expect(q('[data-testid="person-result"]')!.textContent).toContain('Erika Mustermann');
  });

  it('shows the name for an id that the page gives', async () => {
    await create(ID);

    expect(api['peopleByIds']).toHaveBeenCalledWith([ID]);
    expect(q('[data-testid="person-selected"]')!.textContent).toContain('Erika Mustermann');
  });

  it('shows the id itself if the person is unknown or out of reach', async () => {
    api['peopleByIds'].mockResolvedValue([]);
    await create(ID);

    expect(q('[data-testid="person-selected"]')!.textContent).toContain(ID);
  });

  it('forgets the choice when the page takes it away', async () => {
    await create(ID);
    expect(q('[data-testid="person-selected"]')).not.toBeNull();

    fixture.componentRef.setInput('selectedId', null);
    await settle();

    expect(q('[data-testid="person-selected"]')).toBeNull();
    expect(q('[data-testid="person-search"]')).not.toBeNull();
  });

  it('is not usable while it is disabled', async () => {
    await create();
    fixture.componentRef.setInput('disabled', true);
    await settle();

    expect(q<HTMLInputElement>('[data-testid="person-search"]')!.disabled).toBe(true);
  });
});
