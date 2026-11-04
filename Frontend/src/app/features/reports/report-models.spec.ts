import { HttpErrorResponse } from '@angular/common/http';
import { Report, categoryLabel, envelopeMessage, formatCreated, locationNames, severityLabel, statusLabel } from './report-models';

const report = (patch: Partial<Report> = {}): Report => ({
  id: 'r1',
  personnelNumber: 4711,
  status: 'open',
  source: 'employee',
  category: 'theft',
  description: 'Kasse stimmt nicht',
  severity: 'high',
  locations: [
    { id: 'l1', name: 'Augsburg', storeCode: '017', archived: false },
    { id: 'l2', name: 'München', storeCode: '018', archived: false },
  ],
  device: null,
  noticedAt: null,
  noticedHow: null,
  priorActivity: null,
  comment: null,
  archived: false,
  parentReportId: null,
  childReportIds: [],
  createdAt: '2026-11-04T09:30:00',
  updatedAt: '2026-11-04T09:30:00',
  ...patch,
});

describe('labels', () => {
  it('names categories, severities and states in German', () => {
    expect(categoryLabel('virus')).toBe('Virus');
    expect(categoryLabel('bug')).toBe('Fehler');
    expect(categoryLabel('theft')).toBe('Diebstahl');
    expect(categoryLabel('other')).toBe('Sonstiges');
    expect(categoryLabel('suggestion')).toBe('Vorschlag');
    expect(severityLabel('low')).toBe('Niedrig');
    expect(severityLabel('medium')).toBe('Mittel');
    expect(severityLabel('high')).toBe('Hoch');
    expect(severityLabel('critical')).toBe('Kritisch');
    expect(statusLabel('open')).toBe('Offen');
    expect(statusLabel('done')).toBe('Erledigt');
  });

  it('shows an unknown value as it is', () => {
    expect(categoryLabel('new-kind')).toBe('new-kind');
    expect(severityLabel('x')).toBe('x');
  });
});

describe('formatCreated', () => {
  it('shows the day the German way, also over the year change and on the leap day', () => {
    expect(formatCreated('2026-11-04T09:30:00')).toBe('04.11.2026');
    expect(formatCreated('2026-12-31T23:59:59')).toBe('31.12.2026');
    expect(formatCreated('2028-02-29T00:00:00')).toBe('29.02.2028');
  });
});

describe('locationNames', () => {
  it('lists the stores of a report', () => {
    expect(locationNames(report())).toBe('Augsburg, München');
  });

  it('names a report without stores clearly', () => {
    expect(locationNames(report({ locations: [] }))).toBe('Keine Filiale');
  });
});

describe('envelopeMessage', () => {
  it('reads the message of an error answer of the portal', () => {
    const error = new HttpErrorResponse({ status: 403, error: { success: false, message: 'Dafür fehlt dir das Recht.', errors: [] } });
    expect(envelopeMessage(error)).toBe('Dafür fehlt dir das Recht.');
  });

  it('falls back to a general text', () => {
    expect(envelopeMessage(new HttpErrorResponse({ status: 500 }))).toBe('Das hat nicht geklappt. Bitte erneut versuchen.');
    expect(envelopeMessage(new Error('x'))).toBe('Das hat nicht geklappt. Bitte erneut versuchen.');
  });
});
