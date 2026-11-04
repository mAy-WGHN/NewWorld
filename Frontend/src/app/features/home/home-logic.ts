import { Absence, formatPeriod, statusLabel } from '../absences/absence-models';

export type TaskTone = 'danger' | 'info' | 'ok';

export interface Task {
  id: string;
  tone: TaskTone;
  title: string;
  subtitle: string;
  link?: string;
}

export interface TaskInput {
  /** Vacation requests waiting for this person's decision; `null` if unknown or not their task. */
  pendingApprovals: number | null;
  /** Own sick notes without a certificate (AU). */
  missingCertificates: number;
  /** Unread notifications; `null` if unknown. */
  unread: number | null;
}

const plural = (count: number, one: string, many: string): string => `${count} ${count === 1 ? one : many}`;

/** What needs this person's attention, the most pressing first. Nothing open is said so. */
export function buildTasks(input: TaskInput): Task[] {
  const tasks: Task[] = [];
  if (input.missingCertificates > 0) {
    tasks.push({
      id: 'certificates',
      tone: 'danger',
      title: `Für ${plural(input.missingCertificates, 'Krankmeldung', 'Krankmeldungen')} fehlt die AU`,
      subtitle: 'Bitte nachreichen',
      link: '/my-sick-notes',
    });
  }
  if ((input.pendingApprovals ?? 0) > 0) {
    const count = input.pendingApprovals!;
    tasks.push({
      id: 'approvals',
      tone: 'info',
      title: `${plural(count, 'Urlaubsantrag wartet', 'Urlaubsanträge warten')} auf deine Entscheidung`,
      subtitle: 'Genehmigungen',
      link: '/approvals',
    });
  }
  if ((input.unread ?? 0) > 0) {
    tasks.push({
      id: 'unread',
      tone: 'info',
      title: plural(input.unread!, 'ungelesene Mitteilung', 'ungelesene Mitteilungen'),
      subtitle: 'Mitteilungen',
      link: '/notifications',
    });
  }
  return tasks.length > 0 ? tasks : [{ id: 'done', tone: 'ok', title: 'Alles erledigt', subtitle: 'Keine offenen Aufgaben' }];
}

export interface Appointment {
  id: string;
  month: string;
  day: number;
  title: string;
  detail: string;
  note: string;
}

const MONTHS = ['JAN', 'FEB', 'MÄR', 'APR', 'MAI', 'JUN', 'JUL', 'AUG', 'SEP', 'OKT', 'NOV', 'DEZ'];

/** Vacation that is running or still to come (requested or approved), soonest first. `today` is an ISO date. */
export function upcomingAppointments(absences: readonly Absence[], today: string, limit = 3): Appointment[] {
  return absences
    .filter((a) => a.type === 'Vacation' && (a.status === 'Approved' || a.status === 'Requested') && a.to !== null && a.to >= today)
    .sort((a, b) => a.from.localeCompare(b.from))
    .slice(0, limit)
    .map((a) => {
      const [, month, day] = a.from.split('-').map(Number);
      return { id: a.id, month: MONTHS[month - 1], day, title: 'Urlaub', detail: formatPeriod(a.from, a.to), note: statusLabel(a.status) };
    });
}
