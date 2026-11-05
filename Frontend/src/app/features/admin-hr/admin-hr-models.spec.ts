import { HttpErrorResponse } from '@angular/common/http';
import {
  VacationRules,
  WorkingTimeRules,
  describeError,
  seriesRequest,
  thresholdHours,
  toNumber,
  vacationForm,
  vacationRequest,
  workingTimeForm,
  workingTimeRequest,
  wholeNumber,
} from './admin-hr-models';

const audit = { isDefault: true, changedAt: null, changedBy: null };
const defaults: VacationRules = {
  extraDaysExpiryMonth: null, extraDaysExpiryDay: null, waitingPeriodMonths: 0, waitingPeriodMode: 'Accrued', proRataRounding: 'Up', proRataRoundingStep: 0.5, ...audit,
};
const wtDefaults: WorkingTimeRules = { payoutThresholdHours: 0, expiryMonths: null, bookMinusHours: false, maxMinusHours: null, ...audit };

describe('toNumber', () => {
  it('reads numbers with comma or point, empty as null and garbage as undefined', () => {
    expect(toNumber('12')).toBe(12);
    expect(toNumber(' 7,5 ')).toBe(7.5);
    expect(toNumber('')).toBeNull();
    expect(toNumber('abc')).toBeUndefined();
    expect(toNumber('1e3')).toBeUndefined();
  });
});

describe('vacationRequest', () => {
  it('sends the defaults as "never expires, no waiting period, round up to half days"', () => {
    expect(vacationRequest(vacationForm(defaults))).toEqual({
      extraDaysExpiryMonth: null, extraDaysExpiryDay: null, waitingPeriodMonths: 0, waitingPeriodMode: 'Accrued', proRataRounding: 'Up', proRataRoundingStep: 0.5,
    });
  });

  it('sends month and day only when the additional days expire', () => {
    const form = { ...vacationForm(defaults), expires: true, month: '3', day: '31' };
    expect(vacationRequest(form)).toMatchObject({ extraDaysExpiryMonth: 3, extraDaysExpiryDay: 31 });
  });

  it('allows 29 February but rejects days a month does not have', () => {
    const base = { ...vacationForm(defaults), expires: true };
    expect(vacationRequest({ ...base, month: '2', day: '29' })).toMatchObject({ extraDaysExpiryMonth: 2, extraDaysExpiryDay: 29 });
    expect(typeof vacationRequest({ ...base, month: '2', day: '30' })).toBe('string');
    expect(typeof vacationRequest({ ...base, month: '4', day: '31' })).toBe('string');
    expect(typeof vacationRequest({ ...base, month: '13', day: '1' })).toBe('string');
    expect(typeof vacationRequest({ ...base, month: '3', day: '' })).toBe('string');
  });

  it('rejects a waiting period outside 0 to 24 or with decimals', () => {
    const base = vacationForm(defaults);
    expect(typeof vacationRequest({ ...base, waitingMonths: '25' })).toBe('string');
    expect(typeof vacationRequest({ ...base, waitingMonths: '-1' })).toBe('string');
    expect(typeof vacationRequest({ ...base, waitingMonths: '1,5' })).toBe('string');
    expect(vacationRequest({ ...base, waitingMonths: '6' })).toMatchObject({ waitingPeriodMonths: 6 });
  });

  it('rejects a rounding step other than 0.5 or 1', () => {
    expect(typeof vacationRequest({ ...vacationForm(defaults), step: '0.25' })).toBe('string');
    expect(vacationRequest({ ...vacationForm(defaults), step: '1' })).toMatchObject({ proRataRoundingStep: 1 });
  });
});

describe('workingTimeRequest', () => {
  it('sends the defaults: payout always, nothing expires, no minus hours, no limit', () => {
    expect(workingTimeRequest(workingTimeForm(wtDefaults))).toEqual({ payoutThresholdHours: 0, expiryMonths: null, bookMinusHours: false, maxMinusHours: null });
  });

  it('sends expiry and limit only when switched on', () => {
    const form = { ...workingTimeForm(wtDefaults), payout: '20,5', expires: true, expiryMonths: '12', bookMinus: true, limitMinus: true, maxMinus: '10' };
    expect(workingTimeRequest(form)).toEqual({ payoutThresholdHours: 20.5, expiryMonths: 12, bookMinusHours: true, maxMinusHours: 10 });
    expect(workingTimeRequest({ ...form, expires: false, limitMinus: false })).toMatchObject({ expiryMonths: null, maxMinusHours: null });
  });

  it('rejects values outside the ranges and more than two decimals', () => {
    const base = workingTimeForm(wtDefaults);
    expect(typeof workingTimeRequest({ ...base, payout: '1001' })).toBe('string');
    expect(typeof workingTimeRequest({ ...base, payout: '' })).toBe('string');
    expect(typeof workingTimeRequest({ ...base, payout: '1.234' })).toBe('string');
    expect(typeof workingTimeRequest({ ...base, expires: true, expiryMonths: '0' })).toBe('string');
    expect(typeof workingTimeRequest({ ...base, expires: true, expiryMonths: '121' })).toBe('string');
    expect(typeof workingTimeRequest({ ...base, limitMinus: true, maxMinus: '-1' })).toBe('string');
  });
});

describe('thresholdHours', () => {
  it('reads hours, treats empty as "remove" and rejects the rest', () => {
    expect(thresholdHours('160')).toBe(160);
    expect(thresholdHours('')).toBeNull();
    expect(thresholdHours('744')).toBe(744);
    expect(typeof thresholdHours('745')).toBe('string');
    expect(typeof thresholdHours('-1')).toBe('string');
    expect(typeof thresholdHours('1.005')).toBe('string');
    expect(typeof thresholdHours('x')).toBe('string');
  });
});

describe('wholeNumber', () => {
  it('names what is wrong', () => {
    expect(wholeNumber('5', 0, 10, 'Die Frist')).toBe(5);
    expect(wholeNumber('11', 0, 10, 'Die Frist')).toContain('Die Frist');
    expect(wholeNumber('', 0, 10, 'Die Frist')).toContain('0 bis 10');
  });
});

describe('seriesRequest', () => {
  const current = { lastNumber: 15301, startNumber: 1001, endNumber: 99999, remaining: 84698 };

  it('sends only the last number when the range is left out (kept by the service)', () => {
    expect(seriesRequest({ last: '15302', start: '', end: '' }, current)).toEqual({ lastNumber: 15302 });
  });

  it('sends the range when it is typed', () => {
    expect(seriesRequest({ last: '2000', start: '2000', end: '5000' }, current)).toEqual({ lastNumber: 2000, startNumber: 2000, endNumber: 5000 });
  });

  it('rejects an empty or non-numeric last number', () => {
    expect(typeof seriesRequest({ last: '', start: '', end: '' }, current)).toBe('string');
    expect(typeof seriesRequest({ last: '12a', start: '', end: '' }, current)).toBe('string');
    expect(typeof seriesRequest({ last: '1.5', start: '', end: '' }, current)).toBe('string');
  });

  it('rejects a last number outside the range and a reversed range', () => {
    expect(typeof seriesRequest({ last: '100000', start: '', end: '' }, current)).toBe('string');
    expect(typeof seriesRequest({ last: '500', start: '', end: '' }, current)).toBe('string');
    expect(typeof seriesRequest({ last: '1000', start: '', end: '' }, current)).toBe('object'); // one below the start is allowed
    expect(typeof seriesRequest({ last: '3000', start: '4000', end: '3000' }, current)).toBe('string');
  });

  it('uses the defaults 1001 to 99999 when nothing was set yet', () => {
    expect(seriesRequest({ last: '15301', start: '', end: '' }, { lastNumber: null })).toEqual({ lastNumber: 15301 });
    expect(typeof seriesRequest({ last: '100000', start: '', end: '' }, { lastNumber: null })).toBe('string');
  });
});

describe('describeError', () => {
  const problem = (title: string, status = 422) => new HttpErrorResponse({ status, error: { title } });

  it('maps known codes to plain language', () => {
    expect(describeError(problem('InvalidVacationRules'))).toContain('Urlaubsregeln');
    expect(describeError(problem('DuplicateCode', 409))).toContain('schon vergeben');
    expect(describeError(problem('SystemKind'))).toContain('System');
  });

  it('says "not allowed" for a 403 without code and "not reachable" for a lost connection', () => {
    expect(describeError(new HttpErrorResponse({ status: 403 }))).toContain('Das darfst du nicht');
    expect(describeError(new HttpErrorResponse({ status: 0 }))).toContain('nicht erreichbar');
  });

  it('falls back to the generic text and never shows the raw code', () => {
    const text = describeError(problem('SomethingNew'));
    expect(text).toContain('nicht geklappt');
    expect(text).not.toContain('SomethingNew');
  });
});
