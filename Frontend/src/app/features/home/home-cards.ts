export interface HomeChip {
  label: string;
  path: string;
  /** At least one of these privileges is needed; none means everybody. */
  requires?: string[];
}

export interface HomeCard {
  id: string;
  title: string;
  about: string;
  tone: 'blue' | 'green' | 'purple' | 'orange';
  icon: string;
  chips: HomeChip[];
  /** Shown instead of (or below) the links while the area is not built. */
  soon?: string;
}

/** The four areas of the start page. Links only point to areas that exist; the rest is announced, not faked. */
export const HOME_CARDS: HomeCard[] = [
  {
    id: 'do',
    title: 'Erledigen',
    about: 'Meldungen, Anträge und Aufgaben schnell erledigen.',
    tone: 'blue',
    icon: 'tasks',
    chips: [
      { label: 'Krankmeldung', path: '/absences', requires: ['absence:create-own'] },
      { label: 'Urlaub beantragen', path: '/absences', requires: ['absence:create-own'] },
      { label: 'Genehmigungen', path: '/approvals', requires: ['absence:approve'] },
      { label: 'Meldungen bearbeiten', path: '/reports', requires: ['report:read'] },
    ],
  },
  {
    id: 'find',
    title: 'Finden',
    about: 'Wissen, Dokumente und Informationen schnell finden.',
    tone: 'green',
    icon: 'search',
    chips: [{ label: 'FAQ', path: '/faq' }],
    soon: 'Anleitungen, Filialordner und Angebote folgen.',
  },
  {
    id: 'learn',
    title: 'Lernen',
    about: 'Schulungen, Zertifikate und Wissenschecks.',
    tone: 'purple',
    icon: 'cap',
    chips: [],
    soon: 'Dieser Bereich folgt.',
  },
  {
    id: 'mine',
    title: 'Mein Bereich',
    about: 'Deine persönlichen Informationen und Kennzahlen.',
    tone: 'orange',
    icon: 'chart',
    chips: [
      { label: 'Meine Abwesenheiten', path: '/absences', requires: ['absence:read-own', 'absence:create-own'] },
      { label: 'Arbeitszeitkonto', path: '/working-time', requires: ['workingtime:read-own'] },
      { label: 'Meine Karte', path: '/employee-card', requires: ['employee:read-own'] },
      { label: 'Mitteilungen', path: '/notifications', requires: ['notification:read-own'] },
    ],
  },
];
