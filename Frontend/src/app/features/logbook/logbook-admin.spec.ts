import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { AdminAccessApi } from '../admin-access/admin-access.api';
import { PersonPicker } from '../admin-access/person-picker';
import { LogbookAdminPage } from './logbook-admin.page';
import { LogbookReminderAdmin } from './logbook-reminder-admin';
import { LogbookStructureAdmin } from './logbook-structure-admin';
import { LogbookTemplateAdmin } from './logbook-template-admin';
import { LogbookApi } from './logbook.api';
import { LogbookTemplate, ReminderSettings, Structure } from './logbook-models';

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let i = 0; i < 4; i++) {
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r));
  }
}

const refused = (message: string, status = 422): HttpErrorResponse => new HttpErrorResponse({ status, error: { success: false, message, errors: [] } });

function typeInto(root: HTMLElement, selector: string, value: string, event = 'input'): void {
  const field = root.querySelector<HTMLInputElement | HTMLSelectElement>(selector)!;
  field.value = value;
  field.dispatchEvent(new Event(event));
}

describe('LogbookReminderAdmin', () => {
  const settings: ReminderSettings = {
    enabled: true,
    enabledAt: new Date(2026, 9, 20, 8, 0).toISOString(),
    dueDay: 'friday',
    dueTime: '18:00',
    points: [{ daysAfterDue: 0, time: '12:00' }, { daysAfterDue: 3, time: '09:00' }],
    maxReminders: 2,
  };
  let api: Record<'reminderSettings' | 'saveReminderSettings', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<LogbookReminderAdmin>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({ providers: [{ provide: LogbookApi, useValue: api }] });
    fixture = TestBed.createComponent(LogbookReminderAdmin);
    root = fixture.nativeElement as HTMLElement;
    await settle(fixture);
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const save = async (): Promise<void> => {
    q<HTMLButtonElement>('[data-action="save"]')!.click();
    await settle(fixture);
  };

  beforeEach(() => {
    api = { reminderSettings: vi.fn().mockResolvedValue(settings), saveReminderSettings: vi.fn().mockImplementation((s) => Promise.resolve({ ...settings, ...s })) };
  });

  it('shows the saved settings: on since when, due day and time, the points and the maximum', async () => {
    await create();

    expect(q<HTMLInputElement>('[data-testid="enabled"] input')!.checked).toBe(true);
    expect(root.textContent).toContain('Eingeschaltet seit 20.10.2026, 08:00 Uhr');
    expect(q<HTMLSelectElement>('[data-testid="due-day"]')!.value).toBe('friday');
    expect(q<HTMLInputElement>('[data-testid="due-time"]')!.value).toBe('18:00');
    expect(root.querySelectorAll('[data-testid="point"]').length).toBe(2);
    expect(q<HTMLInputElement>('[data-testid="max"]')!.value).toBe('2');
  });

  it('saves what was changed: day, time, a point removed and one added, the maximum, and switching off', async () => {
    await create();

    typeInto(root, '[data-testid="due-day"]', 'thursday', 'change');
    typeInto(root, '[data-testid="due-time"]', '17:30');
    typeInto(root, '[data-testid="max"]', '4');
    root.querySelector<HTMLButtonElement>('[data-action="remove-point"]')!.click(); // the first one goes
    await settle(fixture);
    q<HTMLButtonElement>('[data-action="add-point"]')!.click();
    await settle(fixture);
    q<HTMLInputElement>('[data-testid="enabled"] input')!.click();
    await settle(fixture);
    await save();

    expect(api.saveReminderSettings).toHaveBeenCalledWith({
      enabled: false,
      dueDay: 'thursday',
      dueTime: '17:30',
      points: [{ daysAfterDue: 3, time: '09:00' }, { daysAfterDue: 0, time: '09:00' }],
      maxReminders: 4,
    });
    expect(q('[data-testid="save-status"]')!.textContent).toContain('gespeichert');
  });

  it('changes the days and the time of a point', async () => {
    await create();
    const point = root.querySelectorAll('[data-testid="point"]')[1];
    const [days, time] = [...point.querySelectorAll<HTMLInputElement>('input')];

    days.value = '5';
    days.dispatchEvent(new Event('input'));
    time.value = '08:15';
    time.dispatchEvent(new Event('input'));
    await settle(fixture);
    await save();

    expect(api.saveReminderSettings.mock.calls[0][0].points).toEqual([{ daysAfterDue: 0, time: '12:00' }, { daysAfterDue: 5, time: '08:15' }]);
  });

  it('says that nothing is reminded without a point', async () => {
    await create();
    for (let i = 0; i < 2; i++) {
      q<HTMLButtonElement>('[data-action="remove-point"]')!.click();
      await settle(fixture);
    }

    expect(root.textContent).toContain('es wird nicht automatisch erinnert');
  });

  it('shows what the service refuses and keeps the form as it is', async () => {
    api.saveReminderSettings.mockRejectedValue(new HttpErrorResponse({ status: 422, error: { success: false, message: 'Ungültig', errors: [{ field: 'points', message: 'Ein Zeitpunkt kommt doppelt vor.' }] } }));
    await create();
    typeInto(root, '[data-testid="max"]', '5');
    await save();

    expect(api.saveReminderSettings).toHaveBeenCalledTimes(1);
    expect(q('[data-testid="save-error"]')!.textContent).toContain('doppelt vor');
    expect(q('[data-testid="save-status"]')).toBeNull();
    expect(q<HTMLInputElement>('[data-testid="max"]')!.value).toBe('5');
  });

  it('shows the error if the settings cannot be loaded', async () => {
    api.reminderSettings.mockRejectedValue(refused('Dafür fehlt dir das Recht.', 403));
    await create();

    expect(q('[data-testid="load-error"]')!.textContent).toContain('Recht');
    expect(q('form')).toBeNull();
  });
});

describe('LogbookTemplateAdmin', () => {
  const template: LogbookTemplate = {
    version: 2,
    publishedAt: new Date(2026, 9, 1, 9, 0).toISOString(),
    sections: [
      { id: 's1', title: 'Stimmung', hint: 'Kurz', kind: 'singleChoice', required: true, options: [{ id: 'o1', label: 'Gut' }, { id: 'o2', label: 'Schlecht' }] },
      { id: 's2', title: 'Sonstiges', hint: null, kind: 'freeText', required: false, options: [] },
    ],
  };
  let api: Record<'currentTemplate' | 'saveTemplate', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<LogbookTemplateAdmin>;
  let root: HTMLElement;

  async function create(loaded: LogbookTemplate = template): Promise<void> {
    api.currentTemplate.mockResolvedValue(loaded);
    TestBed.configureTestingModule({ providers: [{ provide: LogbookApi, useValue: api }] });
    fixture = TestBed.createComponent(LogbookTemplateAdmin);
    root = fixture.nativeElement as HTMLElement;
    await settle(fixture);
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const sections = (): Element[] => [...root.querySelectorAll('[data-testid="section"]')];
  async function click(element: Element | null): Promise<void> {
    (element as HTMLElement).click();
    await settle(fixture);
  }
  const save = async (): Promise<void> => click(q('[data-action="save"]'));

  beforeEach(() => {
    // The service answers with the template it saved (what was sent, as version 3).
    api = {
      currentTemplate: vi.fn(),
      saveTemplate: vi.fn().mockImplementation((sent: Record<string, unknown>[]) => Promise.resolve({ version: 3, publishedAt: null, sections: sent })),
    };
  });

  it('shows the sections with their kind, the answers and which version applies', async () => {
    await create();

    expect(q('[data-testid="version-info"]')!.textContent).toContain('Version 2');
    expect(sections().length).toBe(2);
    expect(sections()[0].querySelectorAll('[data-testid="option"]').length).toBe(2);
    expect(sections()[1].querySelector('[data-testid="option"]')).toBeNull(); // a free text has no answers
    expect(sections()[0].querySelector<HTMLSelectElement>('[data-testid="section-kind"]')!.value).toBe('singleChoice');
  });

  it('calls the suggestion by that name before anything was saved', async () => {
    await create({ ...template, version: 0, publishedAt: null });

    expect(q('[data-testid="version-info"]')!.textContent).toContain('Vorschlag');
  });

  it('saves the changes with the ids of what exists, and none for what is new', async () => {
    await create();

    typeInto(sections()[0] as HTMLElement, '[data-testid="section-title"]', 'Stimmung der Woche');
    await settle(fixture);
    await click(sections()[0].querySelector('[data-action="add-option"]'));
    const inputs = sections()[0].querySelectorAll<HTMLInputElement>('[data-testid="option"] input');
    inputs[2].value = 'Mittel';
    inputs[2].dispatchEvent(new Event('input'));
    await settle(fixture);
    await click(sections()[0].querySelector('[data-action="remove-option"]')); // "Gut" goes
    await click(q('[data-action="add-section"]'));
    await save();

    const sent = api.saveTemplate.mock.calls[0][0];
    expect(sent).toHaveLength(3);
    expect(sent[0]).toMatchObject({ id: 's1', title: 'Stimmung der Woche', hint: 'Kurz', required: true });
    expect(sent[0].options).toEqual([{ id: 'o2', label: 'Schlecht' }, { id: null, label: 'Mittel' }]);
    expect(sent[1]).toMatchObject({ id: 's2', kind: 'freeText', options: [] });
    expect(sent[2]).toMatchObject({ id: null, title: '', kind: 'singleChoice', options: [] }); // the empty answers of a new section are left out
    expect(q('[data-testid="save-status"]')!.textContent).toContain('Vorlage ist gespeichert');
    expect(q('[data-testid="version-info"]')!.textContent).toContain('Version 3');
  });

  it('changes the kind and the required flag of a section, and a free text then sends no answers', async () => {
    await create();

    typeInto(sections()[0] as HTMLElement, '[data-testid="section-kind"]', 'freeText', 'change');
    await settle(fixture);
    await click(sections()[0].querySelector('mat-checkbox input'));
    await save();

    expect(sections()[0].querySelector('[data-testid="option"]')).toBeNull();
    expect(api.saveTemplate.mock.calls[0][0][0]).toMatchObject({ id: 's1', kind: 'freeText', required: false, options: [] });
  });

  it('moves sections and answers up and down', async () => {
    await create();

    await click(sections()[0].querySelector('[aria-label="Abschnitt 1 nach unten"]'));
    await save();
    expect(api.saveTemplate.mock.calls[0][0].map((s: { id: string }) => s.id)).toEqual(['s2', 's1']);

    // the saved order shows now: s2 first; move it down again and the answers of s1 around
    await click(sections()[0].querySelector('[aria-label="Abschnitt 1 nach unten"]'));
    await click(sections()[0].querySelector('[aria-label="Antwort 2 nach oben"]'));
    await save();
    expect(api.saveTemplate.mock.calls[1][0].map((s: { id: string }) => s.id)).toEqual(['s1', 's2']);
    expect(api.saveTemplate.mock.calls[1][0][0].options.map((o: { id: string }) => o.id)).toEqual(['o2', 'o1']);
  });

  it('removes a section', async () => {
    await create();

    await click(sections()[1].querySelector('[data-action="remove-section"]'));
    await save();

    expect(api.saveTemplate.mock.calls[0][0].map((s: { id: string }) => s.id)).toEqual(['s1']);
  });

  it('shows what the service refuses', async () => {
    api.saveTemplate.mockRejectedValue(refused('Der Abschnitt braucht eine Überschrift.'));
    await create();

    await save();

    expect(q('[data-testid="save-error"]')!.textContent).toContain('Überschrift');
    expect(q('[data-testid="save-status"]')).toBeNull();
  });
});

describe('LogbookStructureAdmin', () => {
  const structure: Structure = {
    districts: [{ id: 'd1', name: 'Süd', leaders: [{ id: 'p1', name: 'Rudi Revisor' }] }],
    regions: [{ id: 'r1', districtId: 'd1', name: 'Augsburg', leaders: [{ id: 'p2', name: 'Ingo IT' }] }],
    stores: [{ storeId: 'a', storeNumber: '001', storeName: 'Augsburg', districtId: 'd1', regionId: 'r1' }],
  };
  const stores = [
    { id: 'a', name: 'Augsburg', storeCode: '001', archived: false },
    { id: 'b', name: 'München', storeCode: '002', archived: false },
    { id: 'c', name: 'Ulm', storeCode: '003', archived: false },
  ];
  let api: Record<'structure' | 'stores' | 'saveStructure', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<LogbookStructureAdmin>;
  let root: HTMLElement;

  async function create(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [{ provide: LogbookApi, useValue: api }, { provide: AdminAccessApi, useValue: { searchPeople: vi.fn().mockResolvedValue([]), peopleByIds: vi.fn().mockResolvedValue([]) } }],
    });
    fixture = TestBed.createComponent(LogbookStructureAdmin);
    root = fixture.nativeElement as HTMLElement;
    await settle(fixture);
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  async function click(element: Element | null): Promise<void> {
    (element as HTMLElement).click();
    await settle(fixture);
  }
  const save = async (): Promise<void> => click(q('[data-action="save"]'));
  const assignmentSelect = (storeId: string): HTMLSelectElement => q<HTMLSelectElement>(`[data-store="${storeId}"] select`)!;
  const person = (id: string, name: string) => ({ employeeId: id, personnelNumber: '1', name, homeStoreId: null, position: null, exitDate: null });

  beforeEach(() => {
    api = {
      structure: vi.fn().mockResolvedValue(structure),
      stores: vi.fn().mockResolvedValue(stores),
      saveStructure: vi.fn().mockResolvedValue(structure),
    };
  });

  it('shows the districts, regions and leaders and where each store is', async () => {
    await create();

    expect(q<HTMLInputElement>('[data-testid="district-name"]')!.value).toBe('Süd');
    expect(q<HTMLInputElement>('[data-testid="region-name"]')!.value).toBe('Augsburg');
    expect(q('[data-testid="district-leader"]')!.textContent).toContain('Rudi Revisor');
    expect(q('[data-testid="region-leader"]')!.textContent).toContain('Ingo IT');
    expect(assignmentSelect('a').value).toBe('r:r1');
    expect(assignmentSelect('b').value).toBe('');
    expect(q('[data-testid="unassigned"]')!.textContent).toContain('2 Filialen sind noch keinem Bezirk zugeordnet');
    expect([...assignmentSelect('b').options].map((o) => o.textContent!.trim())).toEqual(['Nicht zugeordnet', 'Bezirk Süd', 'Region Augsburg (Süd)']);
  });

  it('assigns a store and saves the hierarchy as a whole, with the district of the region', async () => {
    await create();

    assignmentSelect('b').value = 'r:r1';
    assignmentSelect('b').dispatchEvent(new Event('change'));
    assignmentSelect('c').value = 'd:d1';
    assignmentSelect('c').dispatchEvent(new Event('change'));
    await settle(fixture);
    await save();

    expect(api.saveStructure).toHaveBeenCalledWith({
      districts: [{ id: 'd1', name: 'Süd', leaderIds: ['p1'] }],
      regions: [{ id: 'r1', districtId: 'd1', name: 'Augsburg', leaderIds: ['p2'] }],
      stores: expect.arrayContaining([
        { storeId: 'a', districtId: 'd1', regionId: 'r1' },
        { storeId: 'b', districtId: 'd1', regionId: 'r1' },
        { storeId: 'c', districtId: 'd1', regionId: null },
      ]),
    });
    expect(api.saveStructure.mock.calls[0][0].stores).toHaveLength(3);
    expect(q('[data-testid="save-status"]')!.textContent).toContain('Hierarchie ist gespeichert');
  });

  it('adds a leader that is picked, only once, and removes one', async () => {
    await create();
    const picker = (id: string): PersonPicker => fixture.debugElement.query(By.css(`[data-id="${id}"] app-person-picker`)).componentInstance as PersonPicker;

    picker('r1').picked.emit(person('p3', 'Mia Region'));
    await settle(fixture);
    picker('r1').picked.emit(person('p3', 'Mia Region')); // the same person again
    await settle(fixture);
    expect([...root.querySelectorAll('[data-testid="region-leader"]')].map((c) => c.textContent!.replace('✕', '').trim())).toEqual(['Ingo IT', 'Mia Region']);

    picker('d1').picked.emit(person('p4', 'Dora Distrikt'));
    await settle(fixture);
    await click(root.querySelector('[data-testid="district-leader"] button[aria-label="Rudi Revisor entfernen"]'));
    await save();

    const sent = api.saveStructure.mock.calls[0][0];
    expect(sent.regions[0].leaderIds).toEqual(['p2', 'p3']);
    expect(sent.districts[0].leaderIds).toEqual(['p4']);
  });

  it('starts a new district with a region and saves it with ids made here', async () => {
    api.structure.mockResolvedValue({ districts: [], regions: [], stores: [] });
    await create();
    expect(q('[data-testid="no-districts"]')).not.toBeNull();

    await click(q('[data-action="add-district"]'));
    typeInto(root, '[data-testid="district-name"]', 'Nord');
    await settle(fixture);
    await click(q('[data-action="add-region"]'));
    typeInto(root, '[data-testid="region-name"]', 'Hamburg');
    await settle(fixture);
    assignmentSelect('a').value = [...assignmentSelect('a').options].find((o) => o.textContent!.includes('Hamburg'))!.value;
    assignmentSelect('a').dispatchEvent(new Event('change'));
    await settle(fixture);
    await save();

    const sent = api.saveStructure.mock.calls[0][0];
    expect(sent.districts).toEqual([{ id: expect.stringMatching(/^[0-9a-f-]{36}$/), name: 'Nord', leaderIds: [] }]);
    expect(sent.regions).toEqual([{ id: expect.stringMatching(/^[0-9a-f-]{36}$/), districtId: sent.districts[0].id, name: 'Hamburg', leaderIds: [] }]);
    expect(sent.stores).toEqual([{ storeId: 'a', districtId: sent.districts[0].id, regionId: sent.regions[0].id }]);
  });

  it('removing a region or a district leaves no store assigned to it', async () => {
    await create();

    await click(q('[data-action="remove-region"]'));
    await save();
    expect(api.saveStructure.mock.calls[0][0].regions).toEqual([]);
    expect(api.saveStructure.mock.calls[0][0].stores).toEqual([]); // the store of the removed region is not assigned any more

    api.saveStructure.mockResolvedValue(structure);
    await click(q('[data-action="remove-district"]'));
    await save();
    expect(api.saveStructure.mock.calls[1][0]).toEqual({ districts: [], regions: [], stores: [] });
  });

  it('finds a store by number or name', async () => {
    await create();

    typeInto(root, '[data-testid="store-search"]', 'ulm');
    await settle(fixture);

    expect(root.querySelectorAll('[data-testid="store-row"]').length).toBe(1);
    expect(q('[data-testid="store-row"]')!.textContent).toContain('Ulm');
  });

  it('shows what the service refuses, for example a leader that does not exist', async () => {
    api.saveStructure.mockRejectedValue(refused('Eine Leitung gibt es nicht.'));
    await create();

    await save();

    expect(q('[data-testid="save-error"]')!.textContent).toContain('Leitung');
  });

  it('shows the error if the hierarchy cannot be loaded', async () => {
    api.structure.mockRejectedValue(refused('Dafür fehlt dir das Recht.', 403));
    await create();

    expect(q('[data-testid="load-error"]')).not.toBeNull();
    expect(q('form')).toBeNull();
  });
});

describe('LogbookAdminPage', () => {
  async function tabs(...privileges: string[]): Promise<string[]> {
    const set = new PrivilegeSet(privileges.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { privileges: signal(set) } },
        {
          provide: LogbookApi,
          useValue: {
            structure: vi.fn().mockResolvedValue({ districts: [], regions: [], stores: [] }),
            stores: vi.fn().mockResolvedValue([]),
            currentTemplate: vi.fn().mockResolvedValue({ version: 0, publishedAt: null, sections: [] }),
            reminderSettings: vi.fn().mockResolvedValue({ enabled: true, enabledAt: null, dueDay: 'friday', dueTime: '18:00', points: [], maxReminders: 2 }),
          },
        },
        { provide: AdminAccessApi, useValue: { searchPeople: vi.fn(), peopleByIds: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(LogbookAdminPage);
    await settle(fixture);
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('[role="tab"]')].map((t) => t.textContent!.trim());
  }

  it('shows each part only to the people who hold its privilege', async () => {
    expect(await tabs('logbook:manage-template')).toEqual(['Vorlage']);
  });

  it('shows all three parts to somebody who holds all three privileges', async () => {
    expect(await tabs('logbook:manage-structure', 'logbook:manage-template', 'logbook:manage-reminders')).toEqual(['Hierarchie', 'Vorlage', 'Erinnerung']);
  });
});
