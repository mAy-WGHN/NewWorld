import { HttpErrorResponse } from '@angular/common/http';
import {
  CATEGORIES,
  IssueForm,
  Location,
  buildRequest,
  categoryById,
  combineDateAndTime,
  errorTexts,
  filterLocations,
  locationLabel,
  validateIssue,
} from './report-issue-models';

const form = (patch: Partial<IssueForm> = {}): IssueForm => ({
  personnelNumber: '4711',
  description: 'Etwas ist passiert',
  severity: 'medium',
  device: '',
  noticedDate: '',
  noticedTime: '',
  noticedHow: '',
  priorActivity: '',
  ...patch,
});

const stores: Location[] = [
  { id: 'a', name: 'BaBu Kempten', storeCode: '0011', archived: false },
  { id: 'b', name: 'Samurai Augsburg', storeCode: '0022', archived: false },
  { id: 'c', name: 'HO Head Office', storeCode: '9999', archived: false },
];

describe('the five kinds of report', () => {
  it('are named and ordered like in the existing system', () => {
    expect(CATEGORIES.map((c) => c.label)).toEqual(['Fehler melden', 'Virus melden', 'Diebstahl melden', 'Sonstige Verstöße melden', 'Verbesserungsvorschlag melden']);
    expect(CATEGORIES.map((c) => c.id)).toEqual(['bug', 'virus', 'theft', 'other', 'suggestion']);
  });

  it('ask the description with the question of their kind', () => {
    expect(categoryById('bug').placeholder).toBe('Was funktioniert nicht?');
    expect(categoryById('virus').placeholder).toBe('Was ist aufgefallen?');
    expect(categoryById('theft').placeholder).toBe('Was ist passiert?');
    expect(categoryById('other').placeholder).toBe('Was ist passiert?');
    expect(categoryById('suggestion').placeholder).toBe('Was schlägst du vor?');
  });

  it('let the person choose the severity only for an error, the others have a fixed one', () => {
    expect(categoryById('bug').severity).toBe('choose');
    expect(categoryById('virus').severity).toBe('critical');
    expect(categoryById('theft').severity).toBe('high');
    expect(categoryById('other').severity).toBe('medium');
    expect(categoryById('suggestion').severity).toBe('low');
  });

  it('have the extra fields of their kind', () => {
    expect(categoryById('bug').extra).toBe('none');
    expect(categoryById('virus').extra).toBe('virus');
    expect(categoryById('theft').extra).toBe('noticed');
    expect(categoryById('other').extra).toBe('none');
    expect(categoryById('suggestion').extra).toBe('none');
  });
});

describe('combineDateAndTime', () => {
  it('is nothing without a day and a time', () => {
    expect(combineDateAndTime('', '')).toBeNull();
  });

  it('takes the local day and time and gives a UTC time stamp', () => {
    expect(combineDateAndTime('2026-11-04', '09:30')).toBe(new Date(2026, 10, 4, 9, 30).toISOString());
  });

  it('takes the start of the day for a day without a time', () => {
    expect(combineDateAndTime('2026-11-04', '')).toBe(new Date(2026, 10, 4, 0, 0).toISOString());
  });

  it('takes today for a time without a day', () => {
    const now = new Date(2026, 10, 4, 14, 0);

    expect(combineDateAndTime('', '09:30', now)).toBe(new Date(2026, 10, 4, 9, 30).toISOString());
  });

  it('handles the leap day, the year change and the night', () => {
    expect(combineDateAndTime('2028-02-29', '23:59')).toBe(new Date(2028, 1, 29, 23, 59).toISOString());
    expect(combineDateAndTime('2026-12-31', '00:00')).toBe(new Date(2026, 11, 31, 0, 0).toISOString());
  });
});

describe('validateIssue', () => {
  it('asks for a personnel number, a description and a store, in the words of the existing system', () => {
    expect(validateIssue(form({ personnelNumber: '', description: '  ' }), [])).toEqual({
      personnelNumber: 'Bitte deine Personalnummer angeben.',
      description: 'Bitte kurz beschreiben, worum es geht.',
      locations: 'Bitte mindestens eine Filiale auswählen.',
    });
  });

  it('accepts a complete report', () => {
    expect(validateIssue(form(), ['a'])).toEqual({});
  });

  it('does not accept zero, a negative or a broken personnel number', () => {
    for (const bad of ['0', '-5', 'abc', '1.5']) {
      expect(validateIssue(form({ personnelNumber: bad }), ['a']).personnelNumber).toBe('Bitte deine Personalnummer angeben.');
    }
  });
});

describe('buildRequest', () => {
  it('sends an error with the severity the person chose', () => {
    expect(buildRequest(categoryById('bug'), form({ severity: 'high' }), ['a'])).toEqual({
      personnelNumber: 4711,
      category: 'bug',
      severity: 'high',
      description: 'Etwas ist passiert',
      locationIds: ['a'],
    });
  });

  it('sends the fixed severity of the other kinds, whatever the form says', () => {
    expect(buildRequest(categoryById('virus'), form({ severity: 'low' }), ['a']).severity).toBe('critical');
    expect(buildRequest(categoryById('theft'), form({ severity: 'low' }), ['a']).severity).toBe('high');
    expect(buildRequest(categoryById('other'), form({ severity: 'critical' }), ['a']).severity).toBe('medium');
    expect(buildRequest(categoryById('suggestion'), form({ severity: 'critical' }), ['a']).severity).toBe('low');
  });

  it('sends the extra fields of a virus report, empty ones as nothing', () => {
    const request = buildRequest(categoryById('virus'), form({ device: ' Kasse-03 ', noticedDate: '2026-11-04', noticedTime: '09:30', noticedHow: 'Warnung', priorActivity: '' }), ['a']);

    expect(request).toMatchObject({
      category: 'virus',
      device: 'Kasse-03',
      noticedAt: new Date(2026, 10, 4, 9, 30).toISOString(),
      noticedHow: 'Warnung',
      priorActivity: null,
    });
    expect(buildRequest(categoryById('virus'), form(), ['a'])).toMatchObject({ device: null, noticedAt: null, noticedHow: null, priorActivity: null });
  });

  it('sends the date and time of a theft only', () => {
    const request = buildRequest(categoryById('theft'), form({ noticedDate: '2026-11-04', noticedTime: '18:15', device: 'ignored' }), ['a']);

    expect(request).toMatchObject({ category: 'theft', noticedAt: new Date(2026, 10, 4, 18, 15).toISOString() });
    expect(request).not.toHaveProperty('device');
    expect(buildRequest(categoryById('theft'), form(), ['a'])).toMatchObject({ noticedAt: null });
  });

  it('sends the plain fields for the kinds without extras and trims the description', () => {
    const request = buildRequest(categoryById('suggestion'), form({ description: '  Mehr Licht  ' }), ['a', 'b']);

    expect(request).toEqual({ personnelNumber: 4711, category: 'suggestion', severity: 'low', description: 'Mehr Licht', locationIds: ['a', 'b'] });
  });
});

describe('stores to choose from', () => {
  it('are shown as "Name (Nummer)"', () => {
    expect(locationLabel(stores[0])).toBe('BaBu Kempten (0011)');
  });

  it('are all offered without a search', () => {
    expect(filterLocations(stores, '', []).map((s) => s.id)).toEqual(['a', 'b', 'c']);
  });

  it('are found by name or number, ignoring case', () => {
    expect(filterLocations(stores, 'augs', []).map((s) => s.id)).toEqual(['b']);
    expect(filterLocations(stores, 'KEMPTEN', []).map((s) => s.id)).toEqual(['a']);
    expect(filterLocations(stores, '9999', []).map((s) => s.id)).toEqual(['c']);
    expect(filterLocations(stores, 'xyz', [])).toEqual([]);
  });

  it('do not offer a store twice', () => {
    expect(filterLocations(stores, '', ['a', 'c']).map((s) => s.id)).toEqual(['b']);
  });
});

describe('errorTexts', () => {
  it('uses the message and the messages of the fields of an error answer of the portal', () => {
    const error = new HttpErrorResponse({
      status: 422,
      error: { success: false, message: 'Validierung fehlgeschlagen.', errors: [{ field: 'description', message: 'Darf nicht leer sein.' }] },
    });

    expect(errorTexts(error)).toBe('Validierung fehlgeschlagen. Darf nicht leer sein.');
  });

  it('uses the message alone without field errors and a general text otherwise', () => {
    expect(errorTexts(new HttpErrorResponse({ status: 404, error: { success: false, message: 'Eine Filiale wurde nicht gefunden.', errors: [] } }))).toBe('Eine Filiale wurde nicht gefunden.');
    expect(errorTexts(new HttpErrorResponse({ status: 500 }))).toBe('Das hat nicht geklappt. Bitte erneut versuchen.');
  });

  it('tells about too many reports', () => {
    expect(errorTexts(new HttpErrorResponse({ status: 429 }))).toContain('zu viele');
  });
});
