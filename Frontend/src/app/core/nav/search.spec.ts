import { NavItem } from './nav-items';
import { searchAreas } from './search';

const items: NavItem[] = [
  { label: 'Meine Abwesenheiten', path: 'absences', keywords: ['krank', 'krankmeldung', 'urlaub'], about: 'Krankmeldung, Urlaub und Urlaubskonto.' },
  { label: 'Arbeitszeitkonto', path: 'working-time', keywords: ['überstunden', 'stunden'], about: 'Saldo und Buchungen.' },
  { label: 'Genehmigungen', path: 'approvals', keywords: ['urlaubsantrag'], about: 'Offene Urlaubsanträge entscheiden.' },
  { label: 'FAQ', path: 'faq', about: 'Häufige Fragen und Antworten.' },
];

const paths = (query: string) => searchAreas(query, items).map((i) => i.path);

describe('searchAreas', () => {
  it('finds an area by its name, ignoring case', () => {
    expect(paths('arbeitszeit')).toEqual(['working-time']);
    expect(paths('FAQ')).toEqual(['faq']);
  });

  it('finds an area by what people call it', () => {
    expect(paths('krank')).toContain('absences');
    expect(paths('überstunden')).toEqual(['working-time']);
  });

  it('ignores umlauts and accents on both sides', () => {
    expect(paths('uberstunden')).toEqual(['working-time']);
    expect(paths('Genehmigung')).toEqual(['approvals']);
  });

  it('ranks the name first, then an exact keyword, then the start of a longer word, then the description', () => {
    // "urlaub" is exactly a keyword of absences and only the start of "urlaubsantrag" of approvals
    const result = paths('urlaub');
    expect(result).toEqual(['absences', 'approvals']);

    // a match in the name beats a keyword
    expect(searchAreas('faq', [{ label: 'Anderes', path: 'other', keywords: ['faq'] }, { label: 'FAQ', path: 'faq' }]).map((i) => i.path)).toEqual(['faq', 'other']);
  });

  it('finds nothing for an empty or unknown query', () => {
    expect(paths('')).toEqual([]);
    expect(paths('   ')).toEqual([]);
    expect(paths('xyz')).toEqual([]);
  });

  it('ignores surrounding blanks', () => {
    expect(paths('  faq  ')).toEqual(['faq']);
  });
});
