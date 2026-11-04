import { AccountBucket, addDays, expiringSoon, formatHours, kindLabel, monthLabel } from './working-time-models';

describe('formatHours', () => {
  it('writes hours the German way with a sign', () => {
    expect(formatHours(2.5)).toBe('+2,5 h');
    expect(formatHours(-1)).toBe('−1 h');
    expect(formatHours(0)).toBe('0 h');
    expect(formatHours(12)).toBe('+12 h');
  });

  it('shows at most two decimals and drops needless ones', () => {
    expect(formatHours(1.25)).toBe('+1,25 h');
    expect(formatHours(1.2)).toBe('+1,2 h');
    expect(formatHours(0.333)).toBe('+0,33 h');
  });

  it('can leave the sign out', () => {
    expect(formatHours(7.5, false)).toBe('7,5 h');
    expect(formatHours(-3, false)).toBe('−3 h');
  });
});

describe('kindLabel', () => {
  it('names the kinds of booking', () => {
    expect(kindLabel('OpeningBalance')).toBe('Anfangsbestand');
    expect(kindLabel('Overtime')).toBe('Überstunden');
    expect(kindLabel('TimeOff')).toBe('Freizeitausgleich');
    expect(kindLabel('Payout')).toBe('Auszahlung');
    expect(kindLabel('PayoutAtExit')).toBe('Auszahlung bei Austritt');
    expect(kindLabel('Correction')).toBe('Korrektur');
    expect(kindLabel('MinusHours')).toBe('Minusstunden');
  });

  it('shows an unknown kind as it is', () => {
    expect(kindLabel('Something')).toBe('Something');
  });
});

describe('monthLabel', () => {
  it('names the month in German with the year', () => {
    expect(monthLabel(2026, 11)).toBe('November 2026');
    expect(monthLabel(2027, 1)).toBe('Januar 2027');
    expect(monthLabel(2028, 3)).toBe('März 2028');
  });
});

describe('addDays', () => {
  it('moves an ISO date over month and year boundaries and the leap day', () => {
    expect(addDays('2026-11-04', 60)).toBe('2027-01-03');
    expect(addDays('2028-02-28', 1)).toBe('2028-02-29');
    expect(addDays('2027-02-28', 1)).toBe('2027-03-01');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
  });
});

describe('expiringSoon', () => {
  const bucket = (expiresOn: string | null, remaining = 5): AccountBucket => ({ earnedOn: '2026-01-31', remaining, expiresOn });

  it('lists hours that run out within the next weeks, soonest first', () => {
    const result = expiringSoon([bucket('2026-12-20'), bucket('2026-11-30'), bucket('2027-06-30'), bucket(null)], '2026-11-04', 60);

    expect(result.map((b) => b.expiresOn)).toEqual(['2026-11-30', '2026-12-20']);
  });

  it('includes the last day and leaves out hours already gone, empty ones and ones that do not expire', () => {
    const result = expiringSoon([bucket('2027-01-03'), bucket('2026-11-03'), bucket('2026-12-01', 0), bucket(null)], '2026-11-04', 60);

    expect(result.map((b) => b.expiresOn)).toEqual(['2027-01-03']);
  });

  it('counts across the year change', () => {
    expect(expiringSoon([bucket('2027-01-10')], '2026-12-30', 30)).toHaveLength(1);
  });
});
