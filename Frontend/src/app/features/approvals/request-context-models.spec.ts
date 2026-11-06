import { StoreUnavailability } from '../team-absences/team-absences-models';
import { MAX_WINDOW_DAYS, contextWindow, othersAbsent, othersSummary, windowDays } from './request-context-models';

const row = (id: string, name: string, from: string, to: string | null, kind = 'URLAUB'): StoreUnavailability => ({
  employeeName: name,
  period: { employeeId: id, from, to, status: 'Approved', kind },
});

describe('request context', () => {
  it('shows a few days around the request and cuts very long requests', () => {
    expect(contextWindow('2026-12-23', '2026-12-31')).toEqual({ from: '2026-12-20', to: '2027-01-03' }); // over the turn of the year
    expect(contextWindow('2028-02-28', '2028-03-01')).toEqual({ from: '2028-02-25', to: '2028-03-04' }); // leap year
    const long = contextWindow('2026-01-01', '2026-12-31');
    expect(windowDays(long.from, long.to).length).toBe(MAX_WINDOW_DAYS);
    expect(windowDays('2026-12-23', '2026-12-31')).toHaveLength(15);
  });

  it('builds day columns with weekdays and weekends, also over a clock change', () => {
    const days = windowDays('2026-03-28', '2026-03-29'); // the night of the change to summer time
    expect(days[0].iso).toBe('2026-03-25');
    const saturday = days.find((d) => d.iso === '2026-03-28')!;
    const sunday = days.find((d) => d.iso === '2026-03-29')!;
    expect([saturday.weekday, saturday.weekend]).toEqual(['Sa', true]);
    expect([sunday.weekday, sunday.weekend]).toEqual(['So', true]);
    expect(days.map((d) => d.iso)).toEqual([...new Set(days.map((d) => d.iso))]); // no day twice or missing
  });

  it('counts the colleagues who are away, not the applicant, and a person once per day', () => {
    const rows = [
      row('a', 'Anna', '2026-12-24', '2026-12-26'),
      row('a', 'Anna', '2026-12-25', '2026-12-25', 'KRANK'), // a second period the same day
      row('b', 'Ben', '2026-12-26', null), // open end
      row('me', 'Antragsteller', '2026-12-24', '2026-12-31'),
    ];
    const o = othersAbsent(rows, 'me', '2026-12-24', '2026-12-28');
    expect(o.names).toEqual(['Anna', 'Ben']);
    expect(o.days).toBe(5); // 24, 25, 26 (both), 27 and 28 (Ben, open end)
    expect(o.most).toBe(2); // on the 26th
  });

  it('says in words who is away, and that nobody is', () => {
    expect(othersSummary(othersAbsent([], 'me', '2026-12-24', '2026-12-28'))).toContain('niemand');
    expect(othersSummary(othersAbsent([row('a', 'Anna', '2026-12-24', '2026-12-24')], 'me', '2026-12-24', '2026-12-28'))).toBe('Anna fehlt in diesem Zeitraum an 1 Tag.');
    const two = othersSummary(othersAbsent([row('a', 'Anna', '2026-12-24', '2026-12-28'), row('b', 'Ben', '2026-12-24', '2026-12-28')], 'me', '2026-12-24', '2026-12-28'));
    expect(two).toContain('2 Personen fehlen');
    expect(two).toContain('höchstens 2');
  });

  it('ignores absences outside the requested days', () => {
    const o = othersAbsent([row('a', 'Anna', '2026-12-20', '2026-12-23')], 'me', '2026-12-24', '2026-12-28');
    expect(o.most).toBe(0);
    expect(o.names).toEqual([]);
  });
});
