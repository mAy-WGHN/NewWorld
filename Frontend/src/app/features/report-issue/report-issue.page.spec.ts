import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProfileService } from '../../core/profile.service';
import { ReportsApi } from '../reports/reports.api';
import { LocationsApi } from './locations.api';
import { Location } from './report-issue-models';
import { ReportIssuePage } from './report-issue.page';

const stores: Location[] = [
  { id: 'a', name: 'BaBu Kempten', storeCode: '0011', archived: false },
  { id: 'b', name: 'Samurai Augsburg', storeCode: '0022', archived: false },
];

describe('ReportIssuePage', () => {
  let fixture: ComponentFixture<ReportIssuePage>;
  let root: HTMLElement;
  let create: ReturnType<typeof vi.fn>;
  let list: ReturnType<typeof vi.fn>;
  const personnelNumber = signal<string | null>('15302');

  async function settle(): Promise<void> {
    for (let i = 0; i < 4; i++) {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
    }
  }

  async function open(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: ReportsApi, useValue: { create } },
        { provide: LocationsApi, useValue: { list } },
        { provide: ProfileService, useValue: { personnelNumber } },
      ],
    });
    fixture = TestBed.createComponent(ReportIssuePage);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);

  async function choose(category: string): Promise<void> {
    q<HTMLButtonElement>(`[data-category="${category}"]`)!.click();
    await settle();
  }

  async function type(selector: string, value: string): Promise<void> {
    const field = q<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  async function pickStore(id: string): Promise<void> {
    q('#store-search')!.dispatchEvent(new Event('focus'));
    await settle();
    q<HTMLButtonElement>(`[data-testid="location-option"][data-id="${id}"]`)!.click();
    await settle();
  }

  async function submit(): Promise<void> {
    q<HTMLFormElement>('[data-testid="issue-form"]')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  async function fillCommon(): Promise<void> {
    await type('#description', 'Der Scanner liest nichts mehr');
    await pickStore('a');
  }

  beforeEach(() => {
    personnelNumber.set('15302');
    create = vi.fn().mockResolvedValue({ id: 'r1' });
    list = vi.fn().mockResolvedValue(stores);
  });

  it('asks "Was möchtest du melden?" with the five kinds of the existing system', async () => {
    await open();

    expect(root.textContent).toContain('Was möchtest du melden?');
    const labels = [...root.querySelectorAll('[data-category]')].map((b) => b.textContent?.trim());
    expect(labels).toEqual(['Fehler melden', 'Virus melden', 'Diebstahl melden', 'Sonstige Verstöße melden', 'Verbesserungsvorschlag melden']);
  });

  it('shows the form of the chosen kind with its title and the common fields', async () => {
    await open();
    await choose('bug');

    expect(q('h2')!.textContent).toContain('Fehler melden');
    expect(q('#pn')).not.toBeNull();
    expect(q<HTMLTextAreaElement>('#description')!.placeholder).toBe('Was funktioniert nicht?');
    expect(q('app-store-picker')).not.toBeNull();
    expect(q('[data-category]')).toBeNull();
  });

  it('prefills the personnel number of the signed-in person and leaves it empty if unknown', async () => {
    await open();
    await choose('bug');
    expect(q<HTMLInputElement>('#pn')!.value).toBe('15302');

    TestBed.resetTestingModule();
    personnelNumber.set(null);
    await open();
    await choose('bug');
    expect(q<HTMLInputElement>('#pn')!.value).toBe('');
  });

  it('lets the person choose the severity for an error, "Mittel" first', async () => {
    await open();
    await choose('bug');

    const select = q<HTMLSelectElement>('#severity')!;
    expect(select.value).toBe('medium');
    expect([...select.options].map((o) => o.textContent?.trim())).toEqual(['Niedrig', 'Mittel', 'Hoch', 'Kritisch']);
  });

  it('has the details of a virus: computer, when and how it was noticed, what was done before', async () => {
    await open();
    await choose('virus');

    expect(q<HTMLTextAreaElement>('#description')!.placeholder).toBe('Was ist aufgefallen?');
    expect(root.textContent).toContain('Konkreter Rechner');
    expect(root.textContent).toContain('Falls mehrere vorhanden');
    expect(q<HTMLInputElement>('#device')!.placeholder).toBe('z. B. Kasse-03');
    expect(root.textContent).toContain('Wann bemerkt?');
    expect(q('#noticed-date')).not.toBeNull();
    expect(q('#noticed-time')).not.toBeNull();
    expect(root.textContent).toContain('Wie wurde es bemerkt?');
    expect(root.textContent).toContain('Was wurde davor an dem Gerät gemacht?');
    expect(q('#severity')).toBeNull();
  });

  it('has day and time for a theft and nothing else extra', async () => {
    await open();
    await choose('theft');

    expect(q<HTMLTextAreaElement>('#description')!.placeholder).toBe('Was ist passiert?');
    expect(root.textContent).toContain('Datum & Uhrzeit');
    expect(q('#noticed-date')).not.toBeNull();
    expect(q('#noticed-time')).not.toBeNull();
    expect(q('#device')).toBeNull();
    expect(q('#severity')).toBeNull();
  });

  it('has only the common fields for other violations and suggestions', async () => {
    await open();
    await choose('other');
    expect(q<HTMLTextAreaElement>('#description')!.placeholder).toBe('Was ist passiert?');
    expect(q('#noticed-date')).toBeNull();
    expect(q('#severity')).toBeNull();

    q<HTMLButtonElement>('[data-action="cancel"]')!.click();
    await settle();
    await choose('suggestion');
    expect(q<HTMLTextAreaElement>('#description')!.placeholder).toBe('Was schlägst du vor?');
    expect(q('#noticed-date')).toBeNull();
  });

  it('goes back to the choice with Abbrechen without sending anything', async () => {
    await open();
    await choose('bug');

    q<HTMLButtonElement>('[data-action="cancel"]')!.click();
    await settle();

    expect(q('[data-category]')).not.toBeNull();
    expect(create).not.toHaveBeenCalled();
  });

  it('asks for what is missing in the words of the existing system and sends nothing', async () => {
    await open();
    await choose('bug');
    await type('#pn', '');

    await submit();

    expect(create).not.toHaveBeenCalled();
    expect(root.textContent).toContain('Bitte deine Personalnummer angeben.');
    expect(root.textContent).toContain('Bitte kurz beschreiben, worum es geht.');
    expect(root.textContent).toContain('Bitte mindestens eine Filiale auswählen.');
  });

  it('sends an error with the chosen severity and stores', async () => {
    await open();
    await choose('bug');
    await fillCommon();
    await pickStore('b');
    const select = q<HTMLSelectElement>('#severity')!;
    select.value = 'high';
    select.dispatchEvent(new Event('change', { bubbles: true }));
    await settle();

    await submit();

    expect(create).toHaveBeenCalledWith({
      personnelNumber: 15302,
      category: 'bug',
      severity: 'high',
      description: 'Der Scanner liest nichts mehr',
      locationIds: ['a', 'b'],
    });
  });

  it('sends the fixed severity of each other kind', async () => {
    for (const [kind, severity] of [['virus', 'critical'], ['theft', 'high'], ['other', 'medium'], ['suggestion', 'low']]) {
      TestBed.resetTestingModule();
      create = vi.fn().mockResolvedValue({ id: 'r' });
      await open();
      await choose(kind);
      await fillCommon();

      await submit();

      expect(create).toHaveBeenCalledWith(expect.objectContaining({ category: kind, severity }));
    }
  });

  it('sends the details of a virus report', async () => {
    await open();
    await choose('virus');
    await fillCommon();
    await type('#device', 'Kasse-03');
    await type('#noticed-date', '2026-11-04');
    await type('#noticed-time', '09:30');
    await type('#noticed-how', 'Warnung des Virenscanners');
    await type('#prior-activity', 'USB-Stick eingesteckt');

    await submit();

    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({
        category: 'virus',
        device: 'Kasse-03',
        noticedAt: new Date(2026, 10, 4, 9, 30).toISOString(),
        noticedHow: 'Warnung des Virenscanners',
        priorActivity: 'USB-Stick eingesteckt',
      }),
    );
  });

  it('sends the day and time of a theft', async () => {
    await open();
    await choose('theft');
    await fillCommon();
    await type('#noticed-date', '2026-11-04');
    await type('#noticed-time', '18:15');

    await submit();

    expect(create).toHaveBeenCalledWith(expect.objectContaining({ category: 'theft', noticedAt: new Date(2026, 10, 4, 18, 15).toISOString() }));
  });

  it('confirms in the words of the existing system and offers to report something else', async () => {
    await open();
    await choose('suggestion');
    await fillCommon();

    await submit();

    expect(q('[data-testid="success"]')!.textContent).toContain('Meldung wurde erfolgreich übermittelt.');
    expect(q('[data-testid="issue-form"]')).toBeNull();

    q<HTMLButtonElement>('[data-action="again"]')!.click();
    await settle();
    expect(q('[data-category]')).not.toBeNull();
  });

  it('keeps the form and shows the answer of the portal when it refuses', async () => {
    create.mockRejectedValue(new HttpErrorResponse({ status: 404, error: { success: false, message: 'Eine oder mehrere Filialen wurden nicht gefunden oder sind archiviert.', errors: [] } }));
    await open();
    await choose('bug');
    await fillCommon();

    await submit();

    expect(q('[data-testid="submit-error"]')!.textContent).toContain('Filialen wurden nicht gefunden');
    expect(q('[data-testid="issue-form"]')).not.toBeNull();
    expect(q('[data-testid="success"]')).toBeNull();
  });

  it('does not send twice while it is sending', async () => {
    let finish: (value: unknown) => void = () => undefined;
    create.mockReturnValue(new Promise((resolve) => (finish = resolve)));
    await open();
    await choose('bug');
    await fillCommon();

    await submit();
    await submit();

    expect(create).toHaveBeenCalledTimes(1);
    finish({ id: 'r1' });
    await settle();
  });

  it('tells the person if the stores cannot be loaded', async () => {
    list.mockRejectedValue(new HttpErrorResponse({ status: 500 }));
    await open();
    await choose('bug');

    expect(q('[data-testid="locations-error"]')!.textContent).toContain('Filialen');
  });
});
