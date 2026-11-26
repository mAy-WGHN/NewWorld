import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { PrivilegeSet } from '../../core/auth/privileges';
import { CashRegisterApi } from './cash-register.api';
import { CashRegister, CashRegisterList, cashRegisterError, formatDateTime, isValidTime, kindLabel, sealFormProblem } from './cash-register-models';
import { CashRegistersPage } from './cash-registers.page';

const register = (patch: Partial<CashRegister>): CashRegister => ({
  id: 'r1',
  storeId: 's10',
  storeNumber: '0010',
  storeName: 'Augsburg Oberhausen',
  name: 'WGHN Kasse 1',
  sealed: false,
  stateChangedAt: '2026-11-25T15:52:00Z',
  lastRecordedAt: '2026-11-25T15:52:00Z',
  recordedBy: 'AvS',
  recordedByName: 'Anna von Schmidt',
  sealPhotoId: null,
  sealAttachedBy: null,
  sealAttachedByName: null,
  ...patch,
});

const first = register({});
const second = register({ id: 'r2', storeId: 's12', storeNumber: '0012', storeName: 'Augsburg HBF BaBu', name: 'Kasse 1', sealed: true, stateChangedAt: '2026-11-26T11:50:00Z', sealPhotoId: 'p2', sealAttachedBy: 'EM', sealAttachedByName: 'Erika Mustermann' });

const photo = (type = 'image/png', size = 100) => new File([new Uint8Array(size)], 'siegel.png', { type });
const erika = { id: 'e1', name: 'Erika Mustermann' };

const list = (items: CashRegister[]): CashRegisterList => ({
  summary: { stores: 38, registers: items.length, sealed: items.filter((i) => i.sealed).length, notSealed: items.filter((i) => !i.sealed).length },
  stores: [
    { id: 's10', number: '0010', name: 'Augsburg Oberhausen' },
    { id: 's12', number: '0012', name: 'Augsburg HBF BaBu' },
  ],
  items,
});

const grant = (...keys: string[]) => new PrivilegeSet(keys.map((key) => ({ key, unrestricted: true, storeIds: [], employeeIds: [] })));

describe('cash register models', () => {
  it('names the steps of the history in German and leaves unknown ones as they are', () => {
    expect(kindLabel('recorded')).toBe('Erfasst');
    expect(kindLabel('sealed')).toBe('Siegel erneuert');
    expect(kindLabel('unsealed')).toBe('Siegel aufgehoben');
    expect(kindLabel('deleted')).toBe('Gelöscht');
    expect(kindLabel('neu')).toBe('neu');
  });

  it('says what is missing in the form of a seal: photo first, then the person', () => {
    expect(sealFormProblem({ photo: null, sealedBy: erika })).toContain('Foto');
    expect(sealFormProblem({ photo: photo('text/plain'), sealedBy: erika })).toContain('PNG');
    expect(sealFormProblem({ photo: photo('image/png', 5 * 1024 * 1024 + 1), sealedBy: erika })).toContain('zu groß');
    expect(sealFormProblem({ photo: photo(), sealedBy: null })).toContain('wer das Siegel angebracht hat');
    expect(sealFormProblem({ photo: photo(), sealedBy: erika })).toBeNull();
    expect(sealFormProblem({ photo: photo('image/jpeg', 5 * 1024 * 1024), sealedBy: erika })).toBeNull(); // exactly 5 MB is fine
    expect(sealFormProblem({ photo: photo('image/webp'), sealedBy: erika })).toBeNull();
  });

  it('knows a valid time of day', () => {
    for (const ok of ['00:00', '08:05', '18:00', '23:59']) {
      expect(isValidTime(ok)).toBe(true);
    }
    for (const bad of ['', '8:05', '24:00', '18:60', '18', '18:00 Uhr']) {
      expect(isValidTime(bad)).toBe(false);
    }
  });

  it('formats dates and leaves a broken value as it is', () => {
    expect(formatDateTime('kaputt')).toBe('kaputt');
    expect(formatDateTime('2026-11-26T12:43:00Z')).toMatch(/^\d{2}\.\d{2}\.\d{2}, \d{2}:\d{2}$/);
  });

  it('takes the validation messages of the service, else its message, else a plain text', () => {
    const validation = new HttpErrorResponse({ status: 422, error: { success: false, message: 'Validierung fehlgeschlagen.', errors: [{ field: 'name', message: 'Dieser Wert darf nicht leer sein.' }] } });
    const conflict = new HttpErrorResponse({ status: 409, error: { success: false, message: 'In dieser Filiale gibt es schon eine Kasse mit diesem Namen.', errors: [] } });
    expect(cashRegisterError(validation)).toBe('Dieser Wert darf nicht leer sein.');
    expect(cashRegisterError(conflict)).toContain('schon eine Kasse');
    expect(cashRegisterError(new Error('boom'))).toContain('nicht geklappt');
  });
});

describe('CashRegisterApi', () => {
  let api: CashRegisterApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(CashRegisterApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('unwraps the envelope of the list', async () => {
    const done = api.list();
    http.expectOne('/svc/portal/api/cash-registers').flush({ success: true, data: list([first]) });
    expect((await done).items).toEqual([first]);
  });

  it('sends create, edit, seal and delete to the right places', async () => {
    const created = api.create('s10', 'Kasse 2', { photoId: 'p1', sealedBy: 'e1' });
    const create = http.expectOne('/svc/portal/api/cash-registers');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({ storeId: 's10', name: 'Kasse 2', sealed: true, photoId: 'p1', sealedBy: 'e1' });
    create.flush({ success: true, data: first });

    const plain = api.create('s10', 'Kasse 4');
    const plainCreate = http.expectOne('/svc/portal/api/cash-registers');
    expect(plainCreate.request.body).toEqual({ storeId: 's10', name: 'Kasse 4', sealed: false });
    plainCreate.flush({ success: true, data: first });

    const edited = api.update('r1', 's12', 'Kasse 3', false);
    const edit = http.expectOne('/svc/portal/api/cash-registers/r1');
    expect(edit.request.method).toBe('PUT');
    expect(edit.request.body).toEqual({ storeId: 's12', name: 'Kasse 3', sealed: false });
    edit.flush({ success: true, data: first });

    const editedSealed = api.update('r1', 's12', 'Kasse 3', true, { photoId: 'p3', sealedBy: 'e1' });
    const editSealed = http.expectOne('/svc/portal/api/cash-registers/r1');
    expect(editSealed.request.body).toEqual({ storeId: 's12', name: 'Kasse 3', sealed: true, photoId: 'p3', sealedBy: 'e1' });
    editSealed.flush({ success: true, data: second });

    const sealed = api.seal('r1', { photoId: 'p2', sealedBy: 'e1' });
    const seal = http.expectOne('/svc/portal/api/cash-registers/r1/seal');
    expect(seal.request.method).toBe('POST');
    expect(seal.request.body).toEqual({ photoId: 'p2', sealedBy: 'e1' });
    seal.flush({ success: true, data: second });

    const lifted = api.unseal('r1');
    const unseal = http.expectOne('/svc/portal/api/cash-registers/r1/unseal');
    expect(unseal.request.method).toBe('POST');
    unseal.flush({ success: true, data: first });

    const deleted = api.delete('r1');
    const del = http.expectOne('/svc/portal/api/cash-registers/r1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null, { status: 204, statusText: 'No Content' });

    await Promise.all([created, plain, edited, editedSealed, sealed, lifted, deleted]);
  });

  it('uploads the photo as a form with the file and takes its id', async () => {
    const done = api.uploadPhoto(photo());
    const request = http.expectOne('/svc/portal/api/cash-registers/photos');
    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBe(true);
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);
    request.flush({ success: true, data: { id: 'p9' } });

    expect(await done).toBe('p9');
  });

  it('loads a photo as an image and searches people by name', async () => {
    const image = api.photo('p2');
    const imageRequest = http.expectOne('/svc/portal/api/cash-registers/photos/p2');
    expect(imageRequest.request.responseType).toBe('blob');
    imageRequest.flush(new Blob(['x'], { type: 'image/png' }));
    expect((await image).size).toBe(1);

    const people = api.people('erik');
    const peopleRequest = http.expectOne((r) => r.url === '/svc/portal/api/cash-registers/people');
    expect(peopleRequest.request.params.get('query')).toBe('erik');
    peopleRequest.flush({ success: true, data: [erika] });
    expect(await people).toEqual([erika]);
  });

  it('asks for the history of one store or of all', async () => {
    const everything = api.history();
    const withoutStore = http.expectOne('/svc/portal/api/cash-registers/history');
    expect(withoutStore.request.params.keys()).toEqual([]);
    withoutStore.flush({ success: true, data: [] });
    expect(await everything).toEqual([]);

    const oneStore = api.history('s10');
    const withStore = http.expectOne((r) => r.url === '/svc/portal/api/cash-registers/history');
    expect(withStore.request.params.get('storeId')).toBe('s10');
    withStore.flush({ success: true, data: [] });
    expect(await oneStore).toEqual([]);
  });
});

describe('CashRegistersPage', () => {
  let api: Record<'list' | 'create' | 'update' | 'seal' | 'unseal' | 'delete' | 'history' | 'reminderSettings' | 'saveReminderSettings' | 'uploadPhoto' | 'photo' | 'people', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<CashRegistersPage>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(privileges = grant('cashregister:read', 'cashregister:record', 'cashregister:delete')): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: CashRegisterApi, useValue: api },
        { provide: AuthService, useValue: { privileges: signal(privileges), employeeId: signal('e0'), name: signal('Anna Admin') } },
      ],
    });
    fixture = TestBed.createComponent(CashRegistersPage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const all = (selector: string) => [...root.querySelectorAll<HTMLElement>(selector)];
  const click = async (element: HTMLElement | null) => {
    element!.click();
    await settle();
  };
  const type = async (selector: string, value: string, event = 'input') => {
    const element = q<HTMLInputElement | HTMLSelectElement>(selector)!;
    element.value = value;
    element.dispatchEvent(new Event(event));
    await settle();
  };

  beforeEach(() => {
    api = {
      list: vi.fn().mockResolvedValue(list([first, second])),
      create: vi.fn().mockResolvedValue(first),
      update: vi.fn().mockResolvedValue(first),
      seal: vi.fn().mockResolvedValue(second),
      unseal: vi.fn().mockResolvedValue(first),
      uploadPhoto: vi.fn().mockResolvedValue('p-new'),
      photo: vi.fn().mockResolvedValue(new Blob(['x'], { type: 'image/png' })),
      people: vi.fn().mockResolvedValue([erika]),
      delete: vi.fn().mockResolvedValue(undefined),
      reminderSettings: vi.fn().mockResolvedValue({ enabled: true, time: '18:00' }),
      saveReminderSettings: vi.fn(),
      history: vi.fn().mockResolvedValue([
        { id: 'e1', storeId: 's10', storeNumber: '0010', storeName: 'Augsburg Oberhausen', registerId: 'r1', registerName: 'WGHN Kasse 1', kind: 'recorded', at: '2026-11-25T15:52:00Z', by: 'AvS', byName: 'Anna von Schmidt', photoId: null, attachedBy: null, attachedByName: null },
        { id: 'e2', storeId: 's12', storeNumber: '0012', storeName: 'Augsburg HBF BaBu', registerId: 'r2', registerName: 'Kasse 1', kind: 'sealed', at: '2026-11-26T11:50:00Z', by: 'AvS', byName: 'Anna von Schmidt', photoId: 'p2', attachedBy: 'EM', attachedByName: 'Erika Mustermann' },
      ]),
    };
  });

  it('shows the four numbers and the registers with status, time and initials', async () => {
    await create();

    expect(q('h1')!.textContent).toContain('Kassenregister');
    expect(q('[data-testid="stores-total"]')!.textContent).toContain('38');
    expect(q('[data-testid="registers-total"]')!.textContent).toContain('2');
    expect(q('[data-testid="sealed-total"]')!.textContent).toContain('1');
    expect(q('[data-testid="not-sealed-total"]')!.textContent).toContain('1');
    const rows = all('[data-testid="row"]');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('0010 Augsburg Oberhausen');
    expect(rows[0].textContent).toContain('Nicht versiegelt');
    expect(rows[0].textContent).toContain('AvS');
    expect(rows[1].textContent).toContain('Versiegelt');
  });

  /** The chip of the status filter with this text (the filter is the chips of the library's quick filter). */
  const statusChip = (label: string) => all('wghn-quick-filter mat-chip-option').find((c) => c.textContent?.trim() === label)!.querySelector<HTMLElement>('button')!;

  it('filters by status, by store and by the search text with the library filters', async () => {
    await create();

    await click(statusChip('Versiegelt'));
    expect(all('[data-testid="row"]')).toHaveLength(1);
    expect(all('[data-testid="row"]')[0].textContent).toContain('Kasse 1');

    await click(statusChip('Nicht versiegelt'));
    expect(all('[data-testid="row"]')).toHaveLength(1);
    expect(all('[data-testid="row"]')[0].textContent).toContain('Oberhausen');

    await click(statusChip('Alle'));
    await type('[data-testid="store-filter"]', 's10', 'change');
    expect(all('[data-testid="row"]')).toHaveLength(1);
    expect(all('[data-testid="row"]')[0].textContent).toContain('Oberhausen');

    await type('[data-testid="store-filter"]', '', 'change');
    await type('wghn-table-search input[type="search"]', 'zzz');
    expect(q('[data-testid="empty"]')!.textContent).toContain('passt zu diesen Filtern');

    await type('wghn-table-search input[type="search"]', '0012');
    expect(all('[data-testid="row"]')).toHaveLength(1);
    expect(all('[data-testid="row"]')[0].textContent).toContain('HBF');
  });

  it('sorts by a header, and a second click turns the order around', async () => {
    await create();

    const names = () => all('[data-testid="row"]').map((r) => r.querySelector('th')!.textContent!.trim());
    expect(names()).toEqual(['0010 Augsburg Oberhausen', '0012 Augsburg HBF BaBu']);
    await click(all('th.mat-sort-header').find((h) => h.textContent?.includes('Filiale')) as HTMLElement);
    expect(names()).toEqual(['0012 Augsburg HBF BaBu', '0010 Augsburg Oberhausen']);
    await click(all('th.mat-sort-header').find((h) => h.textContent?.includes('Filiale')) as HTMLElement);
    expect(names()).toEqual(['0010 Augsburg Oberhausen', '0012 Augsburg HBF BaBu']);
  });


  it('records a register: the form asks for store and name and reloads the list', async () => {
    await create();

    await click(q('[data-testid="create-button"]'));
    // store and name are required fields: the browser stops an empty form before it reaches the service
    await click(q('[data-testid="form-save"]'));
    expect(api.create).not.toHaveBeenCalled();
    expect(q('[data-testid="form"]')).not.toBeNull();

    await type('[data-testid="form-store"]', 's12', 'change');
    await type('[data-testid="form-name"]', ' Kasse 2 ');
    await click(q('[data-testid="form-save"]'));

    expect(api.create).toHaveBeenCalledWith('s12', 'Kasse 2', null);
    expect(api.list).toHaveBeenCalledTimes(2);
    expect(q('[data-testid="form"]')).toBeNull();
    expect(q('[data-testid="status"]')!.textContent).toContain('erfasst');
  });

  it('edits a register with its own values and says that the seal falls away', async () => {
    await create();

    await click(all('[data-action="edit"]')[0]);
    expect((q('[data-testid="form-name"]') as HTMLInputElement).value).toBe('WGHN Kasse 1');
    expect(q('[data-testid="form"]')!.textContent).toContain('neu erfasst');
    await type('[data-testid="form-name"]', 'WGHN Kasse 9');
    await click(q('[data-testid="form-save"]'));

    expect(api.update).toHaveBeenCalledWith('r1', 's10', 'WGHN Kasse 9', false, null);
  });

  /** Chooses a file in the photo field of the seal form. */
  const choosePhoto = async (file: File) => {
    const input = q<HTMLInputElement>('[data-testid="seal-fields"] input[type="file"]')!; // the hidden input of the drop zone
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle();
  };

  it('sets a seal with a form: photo and person (the one filling in is the default), the photo goes up first', async () => {
    await create();

    await click(all('[data-action="seal"]')[0]);
    expect(q('[data-testid="seal-form"]')!.textContent).toContain('Siegel anbringen');
    expect(q('[data-testid="person-selected"]')!.textContent).toContain('Anna Admin'); // the default
    expect(q('[data-testid="seal-form"]')!.textContent).toContain('Datum und Uhrzeit werden automatisch gespeichert');

    await choosePhoto(photo());
    await click(q('[data-testid="seal-save"]'));

    expect(api.uploadPhoto).toHaveBeenCalledTimes(1);
    expect(api.seal).toHaveBeenCalledWith('r1', { photoId: 'p-new', sealedBy: 'e0' });
    expect(q('[data-testid="status"]')!.textContent).toContain('Siegel');
    expect(q('[data-testid="seal-form"]')).toBeNull();
  });

  it('takes the photo in the same drop zone as the AU: shows the file, lets it be changed or removed', async () => {
    await create();
    await click(all('[data-action="seal"]')[0]);
    expect(q('[data-testid="file-drop"]')!.textContent).toContain('Datei hierher ziehen oder');

    await choosePhoto(photo());
    expect(q('[data-testid="file-name"]')!.textContent).toContain('siegel.png');
    expect(q('[data-testid="seal-photo-preview"]')).not.toBeNull();

    await click(q('[data-testid="remove-file"]'));
    expect(q('[data-testid="file-name"]')).toBeNull();
    await click(q('[data-testid="seal-save"]'));
    expect(q('[data-testid="error"]')!.textContent).toContain('Foto');
    expect(api.uploadPhoto).not.toHaveBeenCalled();
  });

  it('does not send the form of a seal without a photo or with a wrong file', async () => {
    await create();
    await click(all('[data-action="seal"]')[0]);

    await click(q('[data-testid="seal-save"]'));
    expect(q('[data-testid="error"]')!.textContent).toContain('Foto');

    await choosePhoto(photo('application/pdf'));
    await click(q('[data-testid="seal-save"]'));
    expect(q('[data-testid="error"]')!.textContent).toContain('PNG');

    expect(api.uploadPhoto).not.toHaveBeenCalled();
    expect(api.seal).not.toHaveBeenCalled();
    expect(q('[data-testid="seal-form"]')).not.toBeNull();
  });

  it('lets the person choose somebody else as the one who attached the seal', async () => {
    await create();
    await click(all('[data-action="seal"]')[0]);

    await choosePhoto(photo());
    await click(q('[data-action="clear-person"]'));
    expect(q('[data-testid="person-selected"]')).toBeNull();
    await click(q('[data-testid="seal-save"]'));
    expect(q('[data-testid="error"]')!.textContent).toContain('wer das Siegel angebracht hat'); // nobody chosen yet

    const search = q<HTMLInputElement>('[data-testid="person-search"]')!;
    search.value = 'erik';
    search.dispatchEvent(new Event('input'));
    await new Promise((r) => setTimeout(r, 350)); // the search starts a moment after the last key
    await settle();
    expect(api.people).toHaveBeenCalledWith('erik');
    await click(q('[data-testid="person-result"]'));
    expect(q('[data-testid="person-selected"]')!.textContent).toContain('Erika Mustermann');

    await choosePhoto(photo());
    await click(q('[data-testid="seal-save"]'));
    expect(api.seal).toHaveBeenCalledWith('r1', { photoId: 'p-new', sealedBy: 'e1' });
  });

  it('shows the form of a seal in the form of a register only for a new seal', async () => {
    await create();

    await click(all('[data-action="edit"]')[1]); // sealed already: no new form
    expect(q('[data-testid="seal-fields"]')).toBeNull();
    await click(q('[data-testid="form-cancel"]'));

    await click(all('[data-action="edit"]')[0]); // not sealed: ticking asks for the form
    expect(q('[data-testid="seal-fields"]')).toBeNull();
    q<HTMLInputElement>('[data-testid="form-sealed"]')!.click();
    await settle();
    expect(q('[data-testid="seal-fields"]')).not.toBeNull();
  });

  it('records a new register as sealed with the form, and only when it is complete', async () => {
    await create();

    await click(q('[data-testid="create-button"]'));
    expect(q<HTMLInputElement>('[data-testid="form-sealed"]')!.checked).toBe(false);
    await type('[data-testid="form-store"]', 's10', 'change');
    await type('[data-testid="form-name"]', 'Kasse 7');
    q<HTMLInputElement>('[data-testid="form-sealed"]')!.click();
    await settle();
    await click(q('[data-testid="form-save"]'));
    expect(q('[data-testid="error"]')!.textContent).toContain('Foto');
    expect(api.create).not.toHaveBeenCalled();

    await choosePhoto(photo('image/jpeg'));
    await click(q('[data-testid="form-save"]'));

    expect(api.uploadPhoto).toHaveBeenCalledTimes(1);
    expect(api.create).toHaveBeenCalledWith('s10', 'Kasse 7', { photoId: 'p-new', sealedBy: 'e0' });
  });

  it('edits a register into sealed with the form', async () => {
    await create();

    await click(all('[data-action="edit"]')[0]);
    q<HTMLInputElement>('[data-testid="form-sealed"]')!.click();
    await settle();
    await choosePhoto(photo('image/webp'));
    await click(q('[data-testid="form-save"]'));

    expect(api.update).toHaveBeenCalledWith('r1', 's10', 'WGHN Kasse 1', true, { photoId: 'p-new', sealedBy: 'e0' });
  });

  it('shows who attached the seal on the row and opens the photo of a seal, also from the history', async () => {
    URL.createObjectURL = vi.fn().mockReturnValue('blob:foto');
    URL.revokeObjectURL = vi.fn();
    await create();

    expect(all('[data-testid="seal-info"]')).toHaveLength(1);
    expect(q('[data-testid="seal-info"]')!.textContent).toContain('angebracht von EM');
    expect(all('[data-action="photo"]')).toHaveLength(1); // only the sealed register has one

    await click(all('[data-action="photo"]')[0]);
    expect(api.photo).toHaveBeenCalledWith('p2');
    expect(q('[data-testid="photo-view"] img')!.getAttribute('src')).toBe('blob:foto');
    await click(q('[data-testid="photo-close"]'));
    expect(q('[data-testid="photo-view"]')).toBeNull();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:foto');

    await click(q('[data-testid="history-button"]'));
    expect(q('[data-testid="history-attached"]')!.textContent).toContain('angebracht von EM');
    await click(q('[data-action="history-photo"]'));
    expect(api.photo).toHaveBeenLastCalledWith('p2');
  });

  it('shows a failure of the photo upload and keeps the form', async () => {
    api.uploadPhoto.mockRejectedValue(new HttpErrorResponse({ status: 422, error: { success: false, message: 'Validierung fehlgeschlagen.', errors: [{ field: 'file', message: 'Nur PNG-, JPEG- oder WebP-Bilder sind erlaubt.' }] } }));
    await create();
    await click(all('[data-action="seal"]')[0]);
    await choosePhoto(photo());

    await click(q('[data-testid="seal-save"]'));

    expect(q('[data-testid="error"]')!.textContent).toContain('Nur PNG-');
    expect(api.seal).not.toHaveBeenCalled();
    expect(q('[data-testid="seal-form"]')).not.toBeNull();
  });

  it('lifts the seal only for a sealed register', async () => {
    await create();

    const buttons = all('[data-action="unseal"]');
    expect(buttons).toHaveLength(1); // only the second register is sealed
    await click(buttons[0]);

    expect(api.unseal).toHaveBeenCalledWith('r2');
    expect(q('[data-testid="status"]')!.textContent).toContain('nicht versiegelt');
  });

  it('sets the seal state in the form: shows it for a sealed register and sends the changed tick', async () => {
    await create();

    await click(all('[data-action="edit"]')[1]); // the sealed register
    const tick = q<HTMLInputElement>('[data-testid="form-sealed"]')!;
    expect(tick.checked).toBe(true);
    tick.click();
    await settle();
    await click(q('[data-testid="form-save"]'));

    expect(api.update).toHaveBeenCalledWith('r2', 's12', 'Kasse 1', false, null);
  });

  it('asks before deleting: the first click only asks, the second deletes', async () => {
    await create();

    await click(all('[data-action="delete"]')[0]);
    expect(api.delete).not.toHaveBeenCalled();
    expect(all('[data-action="delete"]')[0].textContent).toContain('Wirklich löschen?');

    await click(all('[data-action="delete"]')[0]);
    expect(api.delete).toHaveBeenCalledWith('r1');
  });

  it('shows a failure of the service as a plain message and keeps the form', async () => {
    api.create.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { success: false, message: 'In dieser Filiale gibt es schon eine Kasse mit diesem Namen.', errors: [] } }));
    await create();

    await click(q('[data-testid="create-button"]'));
    await type('[data-testid="form-store"]', 's10', 'change');
    await type('[data-testid="form-name"]', 'WGHN Kasse 1');
    await click(q('[data-testid="form-save"]'));

    expect(q('[data-testid="error"]')!.textContent).toContain('schon eine Kasse');
    expect(q('[data-testid="form"]')).not.toBeNull();
  });

  it('opens the history of the chosen store and goes back to the list', async () => {
    await create();

    await type('[data-testid="store-filter"]', 's10', 'change');
    await click(q('[data-testid="history-button"]'));

    expect(api.history).toHaveBeenCalledWith('s10');
    expect(all('[data-testid="history-row"]')).toHaveLength(2);
    // newest first (the table sorts the history by time, descending)
    expect(all('[data-testid="history-row"]')[0].textContent).toContain('Siegel erneuert');
    expect(all('[data-testid="history-row"]')[1].textContent).toContain('Erfasst');
    expect(q('[data-testid="table"]')).toBeNull();

    await click(q('[data-testid="history-button"]'));
    expect(q('[data-testid="table"]')).not.toBeNull();
  });

  it('offers only what the privileges allow', async () => {
    await create(grant('cashregister:read'));

    expect(q('[data-testid="create-button"]')).toBeNull();
    // the photo of a seal can be looked at by everybody who reads; nothing that changes anything is offered
    expect(all('[data-action]').map((b) => b.dataset['action'])).toEqual(['photo']);
    expect(all('[data-testid="row"]')).toHaveLength(2);
  });

  it('shows the reminder settings only with the privilege and saves them', async () => {
    api.reminderSettings = vi.fn().mockResolvedValue({ enabled: true, time: '18:00' });
    api.saveReminderSettings = vi.fn().mockImplementation((s) => Promise.resolve(s));
    await create(grant('cashregister:read', 'cashregister:manage-reminders'));

    expect((q('[data-testid="reminder-time"]') as HTMLInputElement).value).toBe('18:00');
    await type('[data-testid="reminder-time"]', '07:45');
    await click(q('[data-testid="reminder-save"]'));

    expect(api.saveReminderSettings).toHaveBeenCalledWith({ enabled: true, time: '07:45' });
    expect(q('[data-testid="reminder-status"]')!.textContent).toContain('07:45');

    q('[data-testid="reminder-enabled"]')!.click();
    await settle();
    await click(q('[data-testid="reminder-save"]'));
    expect(api.saveReminderSettings).toHaveBeenLastCalledWith({ enabled: false, time: '07:45' });
    expect(q('[data-testid="reminder-status"]')!.textContent).toContain('ausgeschaltet');
  });

  it('does not offer the reminder settings without the privilege and refuses a wrong time', async () => {
    api.reminderSettings = vi.fn().mockResolvedValue({ enabled: true, time: '18:00' });
    api.saveReminderSettings = vi.fn();
    await create(grant('cashregister:read'));
    expect(q('[data-testid="reminder"]')).toBeNull();
    expect(api.reminderSettings).not.toHaveBeenCalled();
  });

  it('refuses a time that is not HH:mm before it reaches the service', async () => {
    api.reminderSettings = vi.fn().mockResolvedValue({ enabled: true, time: '18:00' });
    api.saveReminderSettings = vi.fn();
    await create(grant('cashregister:read', 'cashregister:manage-reminders'));

    await type('[data-testid="reminder-time"]', '');
    await click(q('[data-testid="reminder-save"]'));

    expect(api.saveReminderSettings).not.toHaveBeenCalled();
    expect(q('[data-testid="error"]')!.textContent).toContain('HH:mm');
  });

  it('tells when there is no register yet', async () => {
    api.list.mockResolvedValue(list([]));
    await create();

    expect(q('[data-testid="empty"]')!.textContent).toContain('Noch keine Kasse erfasst');
  });

  it('shows the error when the list cannot be loaded', async () => {
    api.list.mockRejectedValue(new HttpErrorResponse({ status: 403, error: { success: false, message: 'Keine Berechtigung.', errors: [] } }));
    await create();

    expect(q('[data-testid="error"]')!.textContent).toContain('Keine Berechtigung');
  });
});
