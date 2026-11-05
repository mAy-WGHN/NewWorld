import { PrivilegeSet } from '../auth/privileges';

export interface NavItem {
  label: string;
  path: string;
  /** At least one of these privileges is needed; none means every signed-in person. */
  requires?: string[];
  /** Short description shown on the placeholder page and the overview of areas until the area is built. */
  about?: string;
  /** What people call it; the search finds the area by these words, too. */
  keywords?: string[];
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
      { label: 'Meine Krankmeldungen', path: 'my-sick-notes', requires: ['absence:read-own', 'absence:create-own'], about: 'Krank melden, eigene Krankmeldungen ansehen und die AU nachreichen.', keywords: ['krank', 'krankmelden', 'krankmeldung', 'au', 'bescheinigung', 'gelber schein'] },
      { label: 'Mein Urlaub', path: 'my-vacation', requires: ['absence:read-own', 'absence:create-own'], about: 'Urlaub beantragen, Urlaubskonto und eigene Urlaubsanträge.', keywords: ['urlaub', 'urlaub beantragen', 'urlaubskonto', 'urlaubstage', 'ferien'] },
      { label: 'Meine Abwesenheiten', path: 'absences', requires: ['absence:read-own', 'absence:create-own'], about: 'Alle eigenen Abwesenheiten auf einen Blick: Urlaub und Krankmeldungen.', keywords: ['fehlen', 'abwesend'] },
      { label: 'Arbeitszeitkonto', path: 'working-time', requires: ['workingtime:read-own'], about: 'Saldo und Buchungen des eigenen Arbeitszeitkontos.', keywords: ['stunden', 'überstunden', 'arbeitszeit', 'saldo'] },
      { label: 'Meine Karte', path: 'employee-card', requires: ['employee:read-own'], about: 'Eigene Stammdaten und Änderungsanträge.', keywords: ['daten', 'adresse', 'stammdaten', 'profil'] },
      { label: 'Mitteilungen', path: 'notifications', requires: ['notification:read-own'], about: 'Deine Mitteilungen und was du erhalten möchtest.', keywords: ['benachrichtigungen', 'nachrichten', 'glocke'] },
    ],
  },
  {
    title: 'Team und Personal',
    items: [
      { label: 'Team-Abwesenheiten', path: 'team-absences', requires: ['absence:read-availability', 'absence:read-reason'], about: 'Wer ist wann nicht verfügbar.', keywords: ['team', 'kollegen', 'wer ist weg', 'dienstplan'] },
      { label: 'Genehmigungen', path: 'approvals', requires: ['absence:approve'], about: 'Offene Urlaubsanträge entscheiden.', keywords: ['genehmigen', 'antrag', 'urlaubsantrag', 'freigeben'] },
      { label: 'Krankmeldungen und AU', path: 'sick-notes', requires: ['certificate:read-status', 'certificate:review'], about: 'Stand der Arbeitsunfähigkeitsbescheinigungen.', keywords: ['au', 'bescheinigung', 'krankschreibung'] },
      { label: 'Mitarbeitende', path: 'employees', requires: ['employee:read', 'employee:decide-change', 'employee:create'], about: 'Mitarbeiterkarten ansehen und anlegen.', keywords: ['personal', 'mitarbeiter', 'anlegen', 'neue mitarbeiter'] },
    ],
  },
  {
    title: 'Portal',
    items: [
      { label: 'Störung melden', path: 'report-issue', about: 'Fehler, Virus, Diebstahl, sonstige Verstöße oder einen Verbesserungsvorschlag melden.', keywords: ['störung', 'fehler', 'virus', 'diebstahl', 'verstoß', 'vorschlag', 'verbesserung', 'problem', 'melden', 'etwas ist vorgefallen'] },
      { label: 'FAQ', path: 'faq', about: 'Häufige Fragen und Antworten.', keywords: ['fragen', 'antworten', 'hilfe', 'anleitung'] },
      { label: 'Meldungen', path: 'reports', requires: ['report:read'], about: 'Eingegangene Meldungen bearbeiten.', keywords: ['meldung', 'incident', 'diebstahl', 'störung'] },
      { label: 'Statistik', path: 'statistics', requires: ['statistic:read'], about: 'Auswertung der Meldungen.', keywords: ['auswertung', 'zahlen', 'kennzahlen'] },
    ],
  },
  {
    title: 'Verwaltung',
    items: [
      { label: 'Rollen und Rechte', path: 'admin/access', requires: ['access:role:read', 'access:assignment:read'], about: 'Rollen, Rechte, Zuständigkeiten und Hauptfilialen.', keywords: ['rollen', 'rechte', 'berechtigung', 'zuständigkeit'] },
      { label: 'Personal-Einstellungen', path: 'admin/hr', requires: ['absence:manage-vacation-rules', 'workingtime:manage-rules', 'employee:manage-lists'], about: 'Urlaubs- und Arbeitszeitregeln, Auswahllisten, Nummernkreis.', keywords: ['urlaubsregeln', 'nummernkreis', 'auswahllisten', 'regeln'] },
      { label: 'Benachrichtigungstexte', path: 'admin/templates', requires: ['template:manage'], about: 'Texte der Benachrichtigungen pflegen.', keywords: ['texte', 'vorlagen', 'mitteilungstexte'] },
    ],
  },
];

export const ALL_ITEMS: NavItem[] = NAV.flatMap((g) => g.items);

/** The groups with only the entries this person may see; empty groups disappear. */
export function visibleNav(privileges: PrivilegeSet): NavGroup[] {
  return NAV.map((g) => ({ ...g, items: g.items.filter((i) => privileges.hasAny(i.requires)) })).filter((g) => g.items.length > 0);
}

/** Every area this person may see, flat (for the search and the home page). */
export function visibleItems(privileges: PrivilegeSet): NavItem[] {
  return ALL_ITEMS.filter((i) => privileges.hasAny(i.requires));
}
