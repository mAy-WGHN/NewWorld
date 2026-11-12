import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LogbookForm } from './logbook-form';
import { LogbookApi } from './logbook.api';
import { LogbookReport, LogbookTemplate, StoreWeek } from './logbook-models';

const template: LogbookTemplate = {
  version: 1,
  publishedAt: null,
  sections: [
    { id: 's1', title: 'Stimmung', hint: 'Wie war die Woche?', kind: 'singleChoice', required: true, options: [{ id: 'o1', label: 'Gut' }, { id: 'o2', label: 'Schlecht' }] },
    { id: 's2', title: 'Was war los?', hint: null, kind: 'multiChoice', required: false, options: [{ id: 'o3', label: 'Krankheit' }, { id: 'o4', label: 'Lieferung' }] },
    { id: 's3', title: 'Sonstiges', hint: null, kind: 'freeText', required: true, options: [] },
  ],
};

const report = (patch: Partial<LogbookReport> = {}): LogbookReport => ({
  id: 'r1',
  storeId: 'st1',
  week: '2026-10-19',
  templateVersion: 1,
  phase: 'submitted',
  state: 'new',
  answers: { s1: { optionIds: ['o1'], text: null }, s3: { optionIds: [], text: 'Alles ruhig' } },
  submittedAt: new Date(2026, 9, 23, 14, 0).toISOString(),
  submittedByName: 'Erika Filiale',
  revisedAt: null,
  revisedByName: null,
  ...patch,
});

const storeWeek = (patch: Partial<StoreWeek> = {}): StoreWeek => ({ storeId: 'st1', storeNumber: '001', storeName: 'Augsburg', templateVersion: 1, canSubmit: true, report: null, ...patch });

describe('LogbookForm', () => {
  let api: Record<'save' | 'submit' | 'followUp', ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<LogbookForm>;
  let root: HTMLElement;
  let changes: number;

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function create(store: StoreWeek): Promise<void> {
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: LogbookApi, useValue: api }] });
    fixture = TestBed.createComponent(LogbookForm);
    fixture.componentRef.setInput('store', store);
    fixture.componentRef.setInput('week', '2026-10-19');
    fixture.componentRef.setInput('template', template);
    changes = 0;
    fixture.componentInstance.changed.subscribe(() => changes++);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const button = (action: string): HTMLButtonElement => q<HTMLButtonElement>(`[data-action="${action}"]`)!;

  async function click(element: HTMLElement): Promise<void> {
    element.click();
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const field = q<HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    await settle();
  }

  const radio = (sectionId: string, label: string): HTMLInputElement =>
    [...root.querySelectorAll<HTMLElement>(`[data-section="${sectionId}"] mat-radio-button`)].find((e) => e.textContent?.includes(label))!.querySelector('input')!;

  const checkbox = (sectionId: string, label: string): HTMLInputElement =>
    [...root.querySelectorAll<HTMLElement>(`[data-section="${sectionId}"] mat-checkbox`)].find((e) => e.textContent?.includes(label))!.querySelector('input')!;

  beforeEach(() => {
    api = {
      save: vi.fn().mockResolvedValue(report()),
      submit: vi.fn().mockResolvedValue(report()),
      followUp: vi.fn().mockResolvedValue({}),
    };
  });

  it('shows every section of the template with its kind of answer and marks the required ones', async () => {
    await create(storeWeek());

    expect(q('h2')!.textContent).toContain('001 Augsburg');
    expect(root.querySelectorAll('[data-section]').length).toBe(3);
    expect(root.querySelectorAll('[data-section="s1"] mat-radio-button').length).toBe(2);
    expect(root.querySelectorAll('[data-section="s2"] mat-checkbox').length).toBe(2);
    expect(q('[data-section="s3"] textarea')).not.toBeNull();
    expect(q('[data-section="s1"] legend')!.textContent).toContain('*');
    expect(q('[data-section="s2"] legend')!.textContent).not.toContain('*');
    expect(q('[data-section="s1"]')!.textContent).toContain('Wie war die Woche?');
    expect(q('[data-testid="phase"]')!.textContent).toContain('Entwurf');
  });

  it('can only be handed in when every required section is answered', async () => {
    await create(storeWeek());
    expect(button('submit').disabled).toBe(true);
    expect(q('[data-testid="missing"]')!.textContent).toContain('2 Pflichtfelder');

    await click(radio('s1', 'Gut'));
    expect(button('submit').disabled).toBe(true);
    expect(q('[data-testid="missing"]')!.textContent).toContain('ein Pflichtfeld');

    await type('[data-section="s3"] textarea', 'Alles ruhig');
    expect(button('submit').disabled).toBe(false);
    expect(q('[data-testid="missing"]')).toBeNull();
  });

  it('saves the draft with what was answered and does not hand in', async () => {
    await create(storeWeek());
    expect(button('save').disabled).toBe(true); // nothing changed yet

    await click(radio('s1', 'Schlecht'));
    await click(checkbox('s2', 'Krankheit'));
    await click(checkbox('s2', 'Lieferung'));
    await click(checkbox('s2', 'Krankheit')); // and off again
    await click(button('save'));

    expect(api.save).toHaveBeenCalledWith('st1', '2026-10-19', { s1: { optionIds: ['o2'], text: null }, s2: { optionIds: ['o4'], text: null } });
    expect(api.submit).not.toHaveBeenCalled();
    expect(q('[data-testid="save-status"]')!.textContent).toContain('Entwurf ist gespeichert');
    expect(changes).toBe(1);
  });

  it('saves first and then hands in, so what was typed is not lost', async () => {
    await create(storeWeek());
    await click(radio('s1', 'Gut'));
    await type('[data-section="s3"] textarea', '  Alles ruhig  ');

    await click(button('submit'));

    expect(api.save).toHaveBeenCalledWith('st1', '2026-10-19', { s1: { optionIds: ['o1'], text: null }, s3: { optionIds: [], text: 'Alles ruhig' } });
    expect(api.submit).toHaveBeenCalledWith('st1', '2026-10-19');
    expect(api.save.mock.invocationCallOrder[0]).toBeLessThan(api.submit.mock.invocationCallOrder[0]);
    expect(q('[data-testid="save-status"]')!.textContent).toContain('abgegeben');
    expect(changes).toBe(1);
  });

  it('does not hand in if saving fails and says why, with the messages of the fields', async () => {
    api.save.mockRejectedValue(new HttpErrorResponse({ status: 422, error: { success: false, message: 'Ungültig', errors: [{ field: 'answers', message: 'Eine Antwort gibt es nicht.' }] } }));
    await create(storeWeek());
    await click(radio('s1', 'Gut'));

    await click(button('save'));

    expect(q('[data-testid="save-error"]')!.textContent).toContain('Eine Antwort gibt es nicht.');
    expect(changes).toBe(0);
    api.save.mockResolvedValue(report());
    await click(radio('s1', 'Schlecht'));
    expect(q('[data-testid="save-error"]')).toBeNull(); // a new change clears the old message
  });

  it('says that handing in is not possible yet if the store has nobody to read it, but the draft can be saved', async () => {
    await create(storeWeek({ canSubmit: false }));
    await click(radio('s1', 'Gut'));
    await type('[data-section="s3"] textarea', 'x');

    expect(q('[data-testid="no-recipients"]')).not.toBeNull();
    expect(button('submit').disabled).toBe(true);
    expect(button('save').disabled).toBe(false);
  });

  it('shows a handed-in report with its answers, who handed it in and the way to the answers of the leaders', async () => {
    await create(storeWeek({ report: report() }));

    expect(q('[data-testid="phase"]')!.textContent).toContain('Abgegeben');
    expect(q('[data-testid="state"]')!.textContent).toContain('Neu');
    expect(q('[data-testid="submitted-info"]')!.textContent).toContain('23.10.2026, 14:00 Uhr');
    expect(q('[data-testid="submitted-info"]')!.textContent).toContain('Erika Filiale');
    expect(q('[data-testid="open-detail"]')!.getAttribute('href')).toBe('/logbook/reports/r1');
    expect(radio('s1', 'Gut').checked).toBe(true);
    expect(q<HTMLTextAreaElement>('[data-section="s3"] textarea')!.value).toBe('Alles ruhig');
    expect(q('[data-action="submit"]')).toBeNull(); // handed in once, only changes can be saved
  });

  it('saves changes of a handed-in report without handing in again', async () => {
    await create(storeWeek({ report: report() }));
    expect(button('save').disabled).toBe(true);

    await type('[data-section="s3"] textarea', 'Doch nicht so ruhig');
    expect(button('save').textContent).toContain('Änderungen speichern');
    await click(button('save'));

    expect(api.save).toHaveBeenCalledWith('st1', '2026-10-19', { s1: { optionIds: ['o1'], text: null }, s3: { optionIds: [], text: 'Doch nicht so ruhig' } });
    expect(api.submit).not.toHaveBeenCalled();
    expect(q('[data-testid="save-status"]')!.textContent).toContain('Änderungen sind gespeichert');
  });

  it('marks the report as followed up only with a note', async () => {
    await create(storeWeek({ report: report() }));
    expect(button('follow-up').disabled).toBe(true);

    await type('[id="note-r1"]', '  Kasse neu eingewiesen ');
    expect(button('follow-up').disabled).toBe(false);
    await click(button('follow-up'));

    expect(api.followUp).toHaveBeenCalledWith('r1', 'Kasse neu eingewiesen');
    expect(q('[data-testid="follow-up"]')!.textContent).toContain('als nachbereitet markiert');
    expect(changes).toBe(1);
  });

  it('shows what follow-up means once it is done, and offers nothing for a report that the leader closed', async () => {
    await create(storeWeek({ report: report({ state: 'followedUp' }) }));
    expect(q('[data-testid="follow-up"]')!.textContent).toContain('Du hast den Bericht als nachbereitet markiert');
    expect(q('[data-action="follow-up"]')).toBeNull();

    fixture.componentRef.setInput('store', storeWeek({ report: report({ state: 'done' }) }));
    await settle();
    expect(q('[data-testid="follow-up"]')!.textContent).toContain('erledigt');
    expect(q('[data-action="follow-up"]')).toBeNull();
  });

  it('offers no follow-up for a draft', async () => {
    await create(storeWeek({ report: report({ phase: 'draft' }) }));

    expect(q('[data-testid="follow-up"]')).toBeNull();
    expect(button('submit')).not.toBeNull();
  });

  it('shows the error of a failed follow-up', async () => {
    api.followUp.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { success: false, message: 'Der Bericht ist schon erledigt.', errors: [] } }));
    await create(storeWeek({ report: report() }));
    await type('[id="note-r1"]', 'x');

    await click(button('follow-up'));

    expect(q('[data-testid="follow-error"]')!.textContent).toContain('schon erledigt');
    expect(changes).toBe(0);
  });

  it('starts from the saved report again when the report changes', async () => {
    await create(storeWeek());
    await click(radio('s1', 'Schlecht'));

    fixture.componentRef.setInput('store', storeWeek({ report: report() }));
    await settle();

    expect(radio('s1', 'Gut').checked).toBe(true);
    expect(radio('s1', 'Schlecht').checked).toBe(false);
  });
});
