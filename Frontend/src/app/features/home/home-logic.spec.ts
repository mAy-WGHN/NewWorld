import { Absence } from '../absences/absence-models';
import { buildTasks, upcomingAppointments } from './home-logic';

describe('buildTasks', () => {
  it('says everything is done when nothing is open', () => {
    const tasks = buildTasks({ pendingApprovals: 0, missingCertificates: 0, unread: 0 });

    expect(tasks).toHaveLength(1);
    expect(tasks[0].tone).toBe('ok');
    expect(tasks[0].title).toBe('Alles erledigt');
  });

  it('treats unknown counts (not loaded, no privilege) as nothing open', () => {
    expect(buildTasks({ pendingApprovals: null, missingCertificates: 0, unread: null })[0].tone).toBe('ok');
  });

  it('lists vacation requests waiting for a decision, in singular and plural', () => {
    expect(buildTasks({ pendingApprovals: 1, missingCertificates: 0, unread: 0 })[0]).toMatchObject({
      title: '1 Urlaubsantrag wartet auf deine Entscheidung',
      link: '/approvals',
    });
    expect(buildTasks({ pendingApprovals: 3, missingCertificates: 0, unread: 0 })[0].title).toBe('3 Urlaubsanträge warten auf deine Entscheidung');
  });

  it('warns about a missing certificate (AU) first', () => {
    const tasks = buildTasks({ pendingApprovals: 2, missingCertificates: 1, unread: 4 });

    expect(tasks.map((t) => t.id)).toEqual(['certificates', 'approvals', 'unread']);
    expect(tasks[0]).toMatchObject({ tone: 'danger', title: 'Für 1 Krankmeldung fehlt die AU', link: '/absences' });
  });

  it('names several missing certificates and unread messages', () => {
    const tasks = buildTasks({ pendingApprovals: 0, missingCertificates: 2, unread: 5 });

    expect(tasks[0].title).toBe('Für 2 Krankmeldungen fehlt die AU');
    expect(tasks[1].title).toBe('5 ungelesene Mitteilungen');
    expect(buildTasks({ pendingApprovals: 0, missingCertificates: 0, unread: 1 })[0].title).toBe('1 ungelesene Mitteilung');
  });
});

const absence = (patch: Partial<Absence>): Absence => ({
  id: 'x',
  employeeId: 'e1',
  type: 'Vacation',
  status: 'Approved',
  from: '2026-12-21',
  to: '2026-12-31',
  certificateStatus: null,
  certifiedThrough: null,
  ...patch,
});

describe('upcomingAppointments', () => {
  const today = '2026-11-04';

  it('lists future and running vacation, soonest first, with month and day', () => {
    const result = upcomingAppointments(
      [absence({ id: 'late', from: '2026-12-21', to: '2026-12-31' }), absence({ id: 'running', from: '2026-11-02', to: '2026-11-06' }), absence({ id: 'soon', from: '2026-11-20', to: '2026-11-22' })],
      today,
    );

    expect(result.map((a) => a.id)).toEqual(['running', 'soon', 'late']);
    expect(result[1]).toMatchObject({ month: 'NOV', day: 20, title: 'Urlaub', detail: '20.11.2026 – 22.11.2026', note: 'Genehmigt' });
  });

  it('leaves out the past, other states, sick notes and open ends', () => {
    const result = upcomingAppointments(
      [
        absence({ id: 'past', from: '2026-10-01', to: '2026-10-05' }),
        absence({ id: 'ended-yesterday', from: '2026-11-01', to: '2026-11-03' }),
        absence({ id: 'rejected', status: 'Rejected' }),
        absence({ id: 'withdrawn', status: 'Withdrawn' }),
        absence({ id: 'sick', type: 'Sickness', status: 'Reported', to: null }),
        absence({ id: 'ends-today', from: '2026-11-04', to: '2026-11-04' }),
      ],
      today,
    );

    expect(result.map((a) => a.id)).toEqual(['ends-today']);
  });

  it('shows a request that is not decided yet', () => {
    const [first] = upcomingAppointments([absence({ status: 'Requested' })], today);

    expect(first.note).toBe('Beantragt');
  });

  it('sorts across the year change', () => {
    const result = upcomingAppointments(
      [absence({ id: 'next-year', from: '2027-01-04', to: '2027-01-08' }), absence({ id: 'this-year', from: '2026-12-28', to: '2026-12-31' })],
      '2026-12-30',
    );

    expect(result.map((a) => a.id)).toEqual(['this-year', 'next-year']);
    expect(result[1].month).toBe('JAN');
    expect(result[1].day).toBe(4);
  });

  it('abbreviates every month in German', () => {
    const months = Array.from({ length: 12 }, (_, i) => `2027-${String(i + 1).padStart(2, '0')}-10`);
    const result = upcomingAppointments(months.map((from, i) => absence({ id: `m${i}`, from, to: from })), '2027-01-01', 12);

    expect(result.map((a) => a.month)).toEqual(['JAN', 'FEB', 'MÄR', 'APR', 'MAI', 'JUN', 'JUL', 'AUG', 'SEP', 'OKT', 'NOV', 'DEZ']);
  });

  it('shows at most the requested number', () => {
    const many = Array.from({ length: 5 }, (_, i) => absence({ id: `a${i}`, from: `2026-12-0${i + 1}`, to: `2026-12-0${i + 1}` }));

    expect(upcomingAppointments(many, today)).toHaveLength(3);
    expect(upcomingAppointments(many, today, 2)).toHaveLength(2);
  });
});
