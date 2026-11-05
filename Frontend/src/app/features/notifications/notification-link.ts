import { PrivilegeSet } from '../../core/auth/privileges';
import { ALL_ITEMS } from '../../core/nav/nav-items';
import { NotificationItem, safeDeepLink } from './notification-models';

/**
 * Where a notification leads, per kind of notification: the first page in the list the person may open.
 * The services send paths of their own ("/absences/{id}", "/employee-change-requests/{id}"); they do not know the pages of the frontend,
 * and several pages (details) do not exist. So the kind decides, in the order of what is most useful for the person.
 */
const TARGETS: Record<string, readonly string[]> = {
  'workforce.vacation-request.supervisor': ['/approvals', '/team-absences'],
  'workforce.vacation-decision.requester': ['/my-vacation', '/absences'],
  'workforce.sick-note.store-manager': ['/sick-notes', '/team-absences'],
  'workforce.sick-note-certificate-reminder.employee': ['/my-sick-notes', '/absences'],
  'workforce.sick-note-certificate-rejected.employee': ['/my-sick-notes', '/absences'],
  'workforce.employee-change-request.hr': ['/employees'],
  'workforce.employee-change-decision.employee': ['/employee-card'],
  'workforce.shift-uncovered-sickness.hr': ['/sick-notes', '/team-absences'],
  'workforce.shift-uncovered-absence.hr': ['/team-absences'],
  'workforce.working-time-payout-reminder.hr': ['/employees', '/working-time'],
  'portal.report-created.handler': ['/reports'],
};

/** The list of notifications is the place every person can open; nothing else is guaranteed. */
const FALLBACK = '/notifications';

/** Whether this person may open the path (areas of the navigation need their privileges; other paths are open). */
function mayOpen(path: string, privileges: PrivilegeSet): boolean {
  const area = ALL_ITEMS.find((item) => path === `/${item.path}`);
  return !area || privileges.hasAny(area.requires);
}

/**
 * The page that opens when the person clicks on a notification, always one that exists and that they may open.
 * Unknown kinds keep the path of the service if it is inside the portal; with no way to go, the list of notifications opens.
 */
export function notificationTarget(item: Pick<NotificationItem, 'templateKey' | 'deepLink'>, privileges: PrivilegeSet): string {
  const known = TARGETS[item.templateKey];
  if (known) {
    return known.find((path) => mayOpen(path, privileges)) ?? FALLBACK;
  }
  const link = safeDeepLink(item.deepLink);
  const base = link ? `/${link.split('/')[1]}` : null;
  return link && base && ALL_ITEMS.some((i) => `/${i.path}` === base) && mayOpen(base, privileges) ? base : FALLBACK;
}
