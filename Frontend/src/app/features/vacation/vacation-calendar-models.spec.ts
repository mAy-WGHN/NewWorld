import { Absence } from '../absences/absence-models';
import { calendarWeeks, inRange, selectionRange, vacationStatusOf } from './vacation-calendar-models';

const vacation = (id: string, from: string, to: string | null, status = 'Approved', type: string | null = 'Vacation'): Absence => ({
  id,
  employeeId: 'e',
  type,
  status,
  from,
  to,
  certificateStatus: null,
  certifiedThrough: null,
});

type Weeks = ReturnType<typeof calendarWeeks>;
const flat = (weeks: Weeks) => weeks.flat();
const day = (weeks: Weeks, iso: string) => flat(weeks).find((d) => d.iso === iso)!;

describe('calendarWeeks', () => {
  it('starts on Monday, has full weeks and marks the days of the neighbouring months', () => {
    const weeks = calendarWeeks({ year: 2026, month: 11 }, [], '2026-11-06');
    expect(weeks.every((w) => w.length === 7)).toBe(true);
    // 1 November 2026 is a Sunday: the first week starts on Monday 26 October.
    expect(weeks[0][0].iso).toBe('2026-10-26');
    expect(weeks[0][0].inMonth).toBe(false);
    expect(weeks[0][6].iso).toBe('2026-11-01');
    expect(weeks[0][6].inMonth).toBe(true);
    expect(weeks.at(-1)![6].iso).toBe('2026-12-06');
  });

  it('marks weekends and today', () => {
    const weeks = calendarWeeks({ year: 2026, month: 11 }, [], '2026-11-06');
    expect(day(weeks, '2026-11-07').weekend).toBe(true);
    expect(day(weeks, '2026-11-09').weekend).toBe(false);
    expect(flat(weeks).filter((d) => d.today).map((d) => d.iso)).toEqual(['2026-11-06']);
  });

  it('handles a leap year February (29 days) and the change of the year', () => {
    const feb = calendarWeeks({ year: 2028, month: 2 }, [], '2028-02-29');
    expect(flat(feb).filter((d) => d.inMonth).length).toBe(29);
    const dec = calendarWeeks({ year: 2026, month: 12 }, [], '2026-12-31');
    expect(day(dec, '2027-01-01').inMonth).toBe(false);
  });

  it('puts a request on every day it covers, also over the end of the month', () => {
    const weeks = calendarWeeks({ year: 2026, month: 12 }, [vacation('a', '2026-12-30', '2027-01-02')], '2026-12-01');
    expect(flat(weeks).filter((d) => d.vacation).map((d) => d.iso)).toEqual(['2026-12-30', '2026-12-31', '2027-01-01', '2027-01-02']);
    expect(day(weeks, '2026-12-30').vacation?.id).toBe('a');
  });

  it('shows only vacation that counts: not sick notes, withdrawn or cancelled', () => {
    const weeks = calendarWeeks(
      { year: 2026, month: 11 },
      [
        vacation('sick', '2026-11-02', '2026-11-03', 'Reported', 'Sickness'),
        vacation('withdrawn', '2026-11-04', '2026-11-05', 'Withdrawn'),
        vacation('cancelled', '2026-11-06', '2026-11-06', 'Cancelled'),
        vacation('ok', '2026-11-10', '2026-11-10', 'Requested'),
      ],
      '2026-11-01',
    );
    expect(flat(weeks).filter((d) => d.vacation).map((d) => d.vacation!.id)).toEqual(['ok']);
  });

  it('lets the stronger status win when requests overlap (approved before requested before rejected)', () => {
    const weeks = calendarWeeks(
      { year: 2026, month: 11 },
      [vacation('r', '2026-11-10', '2026-11-12', 'Rejected'), vacation('q', '2026-11-11', '2026-11-12', 'Requested'), vacation('a', '2026-11-12', '2026-11-12', 'Approved')],
      '2026-11-01',
    );
    expect([day(weeks, '2026-11-10'), day(weeks, '2026-11-11'), day(weeks, '2026-11-12')].map((d) => d.vacation!.id)).toEqual(['r', 'q', 'a']);
  });

  it('treats a request without an end as one day', () => {
    const weeks = calendarWeeks({ year: 2026, month: 11 }, [vacation('x', '2026-11-20', null)], '2026-11-01');
    expect(flat(weeks).filter((d) => d.vacation).map((d) => d.iso)).toEqual(['2026-11-20']);
  });
});

describe('holidays in the calendar', () => {
  const holidays = [
    { date: '2026-12-25', name: '1. Weihnachtstag', storeOpens: false },
    { date: '2026-12-26', name: '2. Weihnachtstag', storeOpens: true },
    { date: '2027-01-01', name: 'Neujahr', storeOpens: false },
  ];

  it('puts the holiday on its day, also in the days of the neighbouring month', () => {
    const weeks = calendarWeeks({ year: 2026, month: 12 }, [], '2026-12-01', holidays);
    expect(day(weeks, '2026-12-25').holiday?.name).toBe('1. Weihnachtstag');
    expect(day(weeks, '2026-12-26').holiday?.storeOpens).toBe(true);
    expect(day(weeks, '2027-01-01').holiday?.name).toBe('Neujahr');
    expect(flat(weeks).filter((d) => d.holiday).length).toBe(3);
  });

  it('shows no holiday without any, and keeps a vacation on a holiday', () => {
    expect(flat(calendarWeeks({ year: 2026, month: 12 }, [], '2026-12-01')).some((d) => d.holiday)).toBe(false);
    const weeks = calendarWeeks({ year: 2026, month: 12 }, [vacation('v', '2026-12-24', '2026-12-28')], '2026-12-01', holidays);
    expect(day(weeks, '2026-12-25').vacation?.id).toBe('v');
    expect(day(weeks, '2026-12-25').holiday).not.toBeNull();
  });
});

describe('vacationStatusOf', () => {
  it('maps the status to what the calendar shows', () => {
    expect(vacationStatusOf('Approved')).toBe('approved');
    expect(vacationStatusOf('Requested')).toBe('requested');
    expect(vacationStatusOf('Rejected')).toBe('rejected');
    expect(vacationStatusOf('Withdrawn')).toBeNull();
    expect(vacationStatusOf('Cancelled')).toBeNull();
  });
});

describe('selectionRange', () => {
  it('orders the two days, so dragging backwards gives the same period', () => {
    expect(selectionRange('2026-11-27', '2026-11-23')).toEqual({ from: '2026-11-23', to: '2026-11-27' });
    expect(selectionRange('2026-11-23', '2026-11-27')).toEqual({ from: '2026-11-23', to: '2026-11-27' });
  });

  it('is one day for a single click, also at the change of the year', () => {
    expect(selectionRange('2026-12-31', '2026-12-31')).toEqual({ from: '2026-12-31', to: '2026-12-31' });
    expect(selectionRange('2027-01-02', '2026-12-30')).toEqual({ from: '2026-12-30', to: '2027-01-02' });
  });

  it('tells whether a day is inside', () => {
    const range = { from: '2026-11-23', to: '2026-11-27' };
    expect(inRange('2026-11-23', range)).toBe(true);
    expect(inRange('2026-11-27', range)).toBe(true);
    expect(inRange('2026-11-28', range)).toBe(false);
    expect(inRange('2026-11-23', null)).toBe(false);
  });
});
