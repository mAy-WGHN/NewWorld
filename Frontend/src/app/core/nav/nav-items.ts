import { PrivilegeSet } from '../auth/privileges';

export interface NavItem {
  label: string;
  path: string;
  /** At least one of these privileges is needed; none means every signed-in person. */
  requires?: string[];
  /** Short description shown on the placeholder page until the area is built. */
  about?: string;
}

export interface NavGroup {
  title: string;
  items: NavItem[];
}

/** The areas of the portal and who sees them. Keys are the privileges the services register in Access. */
export const NAV: NavGroup[] = [
  {
    title: 'Persönlich',
    items: [
      { label: 'Meine Abwesenheiten', path: 'absences', requires: ['absence:read-own', 'absence:create-own'], about: 'Krankmeldung, Urlaub und Urlaubskonto.' },
      { label: 'Arbeitszeitkonto', path: 'working-time', requires: ['workingtime:read-own'], about: 'Saldo und Buchungen des eigenen Arbeitszeitkontos.' },
      { label: 'Meine Karte', path: 'employee-card', requires: ['employee:read-own'], about: 'Eigene Stammdaten und Änderungsanträge.' },
      { label: 'Benachrichtigungen', path: 'notifications', requires: ['notification:read-own'], about: 'Mitteilungen und Einstellungen.' },
    ],
  },
  {
    title: 'Team und Personal',
    items: [
      { label: 'Team-Abwesenheiten', path: 'team-absences', requires: ['absence:read-availability', 'absence:read-reason'], about: 'Wer ist wann nicht verfügbar.' },
      { label: 'Genehmigungen', path: 'approvals', requires: ['absence:approve'], about: 'Offene Urlaubsanträge entscheiden.' },
      { label: 'Krankmeldungen und AU', path: 'sick-notes', requires: ['certificate:read-status', 'certificate:review'], about: 'Stand der Arbeitsunfähigkeitsbescheinigungen.' },
      { label: 'Mitarbeitende', path: 'employees', requires: ['employee:read'], about: 'Mitarbeiterkarten ansehen und anlegen.' },
    ],
  },
  {
    title: 'Portal',
    items: [
      { label: 'FAQ', path: 'faq', about: 'Häufige Fragen und Antworten.' },
      { label: 'Meldungen', path: 'reports', requires: ['report:read'], about: 'Eingegangene Meldungen bearbeiten.' },
      { label: 'Statistik', path: 'statistics', requires: ['statistic:read'], about: 'Auswertung der Meldungen.' },
    ],
  },
  {
    title: 'Verwaltung',
    items: [
      { label: 'Rollen und Rechte', path: 'admin/access', requires: ['access:role:read', 'access:assignment:read'], about: 'Rollen, Rechte, Zuständigkeiten und Hauptfilialen.' },
      { label: 'Personal-Einstellungen', path: 'admin/hr', requires: ['absence:manage-vacation-rules', 'workingtime:manage-rules', 'employee:manage-lists'], about: 'Urlaubs- und Arbeitszeitregeln, Auswahllisten, Nummernkreis.' },
      { label: 'Benachrichtigungstexte', path: 'admin/templates', requires: ['template:manage'], about: 'Texte der Benachrichtigungen pflegen.' },
    ],
  },
];

export const ALL_ITEMS: NavItem[] = NAV.flatMap((g) => g.items);

/** The groups with only the entries this person may see; empty groups disappear. */
export function visibleNav(privileges: PrivilegeSet): NavGroup[] {
  return NAV.map((g) => ({ ...g, items: g.items.filter((i) => privileges.hasAny(i.requires)) })).filter((g) => g.items.length > 0);
}
