import { NavItem } from './nav-items';

/** Lower case, without accents and umlauts, so "Überstunden" and "uberstunden" match. */
const normalize = (text: string): string => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();

function score(item: NavItem, query: string): number {
  const label = normalize(item.label);
  if (label.startsWith(query)) {
    return 5;
  }
  if (label.includes(query)) {
    return 4;
  }
  const keywords = (item.keywords ?? []).map(normalize);
  if (keywords.includes(query)) {
    return 3;
  }
  if (keywords.some((k) => k.startsWith(query))) {
    return 2;
  }
  if (keywords.some((k) => k.includes(query)) || normalize(item.about ?? '').includes(query)) {
    return 1;
  }
  return 0;
}

/** The areas that fit the query, best match first (name, then what people call it, then the description). */
export function searchAreas(query: string, items: readonly NavItem[]): NavItem[] {
  const q = normalize(query);
  if (!q) {
    return [];
  }
  return items
    .map((item) => ({ item, score: score(item, q) }))
    .filter((r) => r.score > 0)
    .sort((a, b) => b.score - a.score || a.item.label.localeCompare(b.item.label, 'de'))
    .map((r) => r.item);
}
