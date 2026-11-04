import {
  Absence,
  canEnd,
  canUpload,
  canWithdraw,
  certificateLabel,
  errorMessage,
  formatDate,
  formatPeriod,
  problemTitle,
  statusLabel,
  typeLabel,
  validatePeriod,
} from './absence-models';
import { HttpErrorResponse } from '@angular/common/http';

const absence = (patch: Partial<Absence>): Absence => ({
  id: 'a1',
  employeeId: 'e1',
  type: 'Vacation',
  status: 'Approved',
  from: '2026-12-01',
  to: '2026-12-05',
  certificateStatus: null,
  certifiedThrough: null,
  ...patch,
});

describe('labels', () => {
  it('names types and states in German', () => {
    expect(typeLabel('Sickness')).toBe('Krankmeldung');
    expect(typeLabel('Vacation')).toBe('Urlaub');
    expect(typeLabel(null)).toBe('Abwesenheit');
    expect(statusLabel('Requested')).toBe('Beantragt');
    expect(statusLabel('Approved')).toBe('Genehmigt');
    expect(statusLabel('Rejected')).toBe('Abgelehnt');
    expect(statusLabel('Withdrawn')).toBe('Zurückgezogen');
    expect(statusLabel('Cancelled')).toBe('Storniert');
    expect(statusLabel('Reported')).toBe('Gemeldet');
  });

  it('shows an unknown state as it is rather than hiding it', () => {
    expect(statusLabel('Something')).toBe('Something');
  });

  it('names the certificate states and nothing for non-sick notes', () => {
    expect(certificateLabel('Missing')).toBe('AU fehlt');
    expect(certificateLabel('Submitted')).toBe('AU eingereicht');
    expect(certificateLabel('Verified')).toBe('AU geprüft');
    expect(certificateLabel('Rejected')).toBe('AU abgelehnt');
    expect(certificateLabel('Unknown')).toBe('');
    expect(certificateLabel(null)).toBe('');
  });
});

describe('dates', () => {
  it('formats an ISO date the German way, also across the year and leap day', () => {
    expect(formatDate('2026-11-04')).toBe('04.11.2026');
    expect(formatDate('2028-02-29')).toBe('29.02.2028');
    expect(formatDate('2026-12-31')).toBe('31.12.2026');
    expect(formatDate(null)).toBe('');
  });

  it('formats a period; an open end is shown as open', () => {
    expect(formatPeriod('2026-12-01', '2026-12-05')).toBe('01.12.2026 – 05.12.2026');
    expect(formatPeriod('2026-12-01', '2026-12-01')).toBe('01.12.2026');
    expect(formatPeriod('2026-12-01', null)).toBe('ab 01.12.2026 (offen)');
  });
});

describe('validatePeriod', () => {
  it('needs a start', () => {
    expect(validatePeriod('', '2026-12-05', true)).toBe('Bitte einen Beginn angeben.');
  });

  it('needs an end only where required', () => {
    expect(validatePeriod('2026-12-01', '', true)).toBe('Bitte ein Ende angeben.');
    expect(validatePeriod('2026-12-01', '', false)).toBeNull();
  });

  it('rejects an end before the start, also over a year change', () => {
    expect(validatePeriod('2026-12-31', '2026-12-30', true)).toBe('Das Ende liegt vor dem Beginn.');
    expect(validatePeriod('2026-12-31', '2027-01-02', true)).toBeNull();
  });

  it('accepts a single day', () => {
    expect(validatePeriod('2026-12-01', '2026-12-01', true)).toBeNull();
  });
});

describe('which actions a row offers', () => {
  it('lets a person withdraw vacation that is requested or approved', () => {
    expect(canWithdraw(absence({ status: 'Requested' }))).toBe(true);
    expect(canWithdraw(absence({ status: 'Approved' }))).toBe(true);
    expect(canWithdraw(absence({ status: 'Rejected' }))).toBe(false);
    expect(canWithdraw(absence({ status: 'Withdrawn' }))).toBe(false);
    expect(canWithdraw(absence({ type: 'Sickness', status: 'Reported' }))).toBe(false);
  });

  it('lets a person end an open sick note only', () => {
    expect(canEnd(absence({ type: 'Sickness', status: 'Reported', to: null }))).toBe(true);
    expect(canEnd(absence({ type: 'Sickness', status: 'Reported', to: '2026-12-05' }))).toBe(false);
    expect(canEnd(absence({ type: 'Vacation', to: null }))).toBe(false);
  });

  it('lets a person upload a certificate for a reported sick note', () => {
    expect(canUpload(absence({ type: 'Sickness', status: 'Reported' }))).toBe(true);
    expect(canUpload(absence({ type: 'Sickness', status: 'Cancelled' }))).toBe(false);
    expect(canUpload(absence({ type: 'Vacation', status: 'Approved' }))).toBe(false);
  });
});

describe('errors', () => {
  it('explains the errors of the service in plain words', () => {
    expect(errorMessage('InsufficientBalance')).toContain('Urlaubstage');
    expect(errorMessage('Overlap')).toContain('überschneidet');
    expect(errorMessage('InvalidPeriod')).toContain('Zeitraum');
    expect(errorMessage('AfterExit')).toContain('Austritt');
    expect(errorMessage('NoApprover')).toContain('Genehmigung');
    expect(errorMessage('WaitingPeriod')).toContain('Wartezeit');
    expect(errorMessage('FileTooLarge')).toContain('groß');
    expect(errorMessage('UnsupportedFileType')).toContain('PDF');
  });

  it('falls back to a general message for unknown errors', () => {
    expect(errorMessage('SomethingNew')).toBe('Das hat nicht geklappt. Bitte erneut versuchen.');
    expect(errorMessage(undefined)).toBe('Das hat nicht geklappt. Bitte erneut versuchen.');
  });

  it('reads the title of a problem answer', () => {
    const error = new HttpErrorResponse({ status: 422, error: { title: 'Overlap' } });
    expect(problemTitle(error)).toBe('Overlap');
    expect(problemTitle(new HttpErrorResponse({ status: 500 }))).toBeUndefined();
    expect(problemTitle(new Error('x'))).toBeUndefined();
  });
});
