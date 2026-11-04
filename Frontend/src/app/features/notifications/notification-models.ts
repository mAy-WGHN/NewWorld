/** An in-app notification of the person (`GET /api/me/notifications`). Times come with their offset (`...+00:00`). */
export interface NotificationItem {
  id: string;
  templateKey: string;
  title: string;
  body: string;
  /** A path inside the portal the notification points to, if any. */
  deepLink: string | null;
  createdAt: string;
  readAt: string | null;
}

export interface ChannelPreference {
  channel: string;
  enabled: boolean;
}

/** A kind of notification with the person's choice per channel; mandatory kinds cannot be switched off. */
export interface NotificationType {
  templateKey: string;
  mandatory: boolean;
  channels: ChannelPreference[];
}

const TYPES: Record<string, string> = {
  'portal.report-created.handler': 'Neue Meldung im Portal',
  'workforce.employee-change-decision.employee': 'Entscheidung über deinen Änderungsantrag',
  'workforce.employee-change-request.hr': 'Neuer Änderungsantrag zu entscheiden',
  'workforce.shift-uncovered-absence.hr': 'Schichten wegen Abwesenheit anzupassen',
  'workforce.shift-uncovered-sickness.hr': 'Schichten wegen Krankheit anzupassen',
  'workforce.sick-note-certificate-rejected.employee': 'AU wurde abgelehnt',
  'workforce.sick-note-certificate-reminder.employee': 'Erinnerung: AU nachreichen',
  'workforce.sick-note.store-manager': 'Krankmeldung in deiner Filiale',
  'workforce.vacation-decision.requester': 'Entscheidung über deinen Urlaubsantrag',
  'workforce.vacation-request.supervisor': 'Neuer Urlaubsantrag zu entscheiden',
  'workforce.working-time-payout-reminder.hr': 'Erinnerung: Arbeitszeit auszahlen',
};

const CHANNELS: Record<string, string> = { InApp: 'Im Portal', Push: 'Push', Email: 'E-Mail' };

/** An unknown key is shown as it is, so a new kind of notification never disappears silently. */
export const typeLabel = (key: string): string => TYPES[key] ?? key;

export const channelLabel = (channel: string): string => CHANNELS[channel] ?? channel;

const two = (n: number): string => String(n).padStart(2, '0');

/** Day and time in the local zone: `04.11.2026 09:30`. */
export function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return `${two(d.getDate())}.${two(d.getMonth() + 1)}.${d.getFullYear()} ${two(d.getHours())}:${two(d.getMinutes())}`;
}

/** Only paths inside the portal are links; everything else (another site, a script) is not. */
export function safeDeepLink(link: string | null): string | null {
  return link && link.startsWith('/') && !link.startsWith('//') ? link : null;
}
