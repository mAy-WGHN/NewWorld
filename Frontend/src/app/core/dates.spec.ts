import { todayIso } from './dates';

describe('todayIso', () => {
  it('uses the local calendar day, padded', () => {
    expect(todayIso(new Date(2026, 10, 4, 23, 59))).toBe('2026-11-04');
    expect(todayIso(new Date(2026, 0, 5, 0, 1))).toBe('2026-01-05');
  });

  it('handles the year change and the leap day', () => {
    expect(todayIso(new Date(2026, 11, 31, 12))).toBe('2026-12-31');
    expect(todayIso(new Date(2027, 0, 1, 0, 0))).toBe('2027-01-01');
    expect(todayIso(new Date(2028, 1, 29, 12))).toBe('2028-02-29');
  });
});
