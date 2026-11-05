/** An FAQ entry of the portal (`GET /api/faqs`). The answer (`description`) is Markdown. */
export interface Faq {
  id: string;
  title: string;
  description: string;
  order: number;
  archived: boolean;
  createdAt: string;
}

export interface FaqImageUpload {
  token: string;
  url: string;
}

/** `orphaned` = no entry uses it, `archived` = only an archived entry, `active` = an active entry shows it. */
export type FaqImageStatus = 'orphaned' | 'archived' | 'active';

export interface FaqImage {
  id: string;
  url: string;
  createdAt: string;
  status: string;
  faqId: string | null;
  faqTitle: string | null;
}

export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
export const IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/webp'];
export const IMAGE_ACCEPT = IMAGE_TYPES.join(',');

/** Why an image cannot be uploaded (the portal checks the content again), or null if it can. */
export function imageProblem(file: { type: string; size: number }): string | null {
  if (!IMAGE_TYPES.includes(file.type)) {
    return 'Nur PNG-, JPEG- oder WebP-Bilder sind erlaubt.';
  }
  if (file.size > MAX_IMAGE_BYTES) {
    return 'Die Datei ist zu groß. Erlaubt sind höchstens 5 MB.';
  }
  return null;
}

const IMAGE_STATUSES: Record<string, string> = { orphaned: 'Nicht verwendet', archived: 'Nur in archivierter Antwort', active: 'In Verwendung' };
export const imageStatusLabel = (status: string): string => IMAGE_STATUSES[status] ?? status;

/** Entries that contain the text (case does not matter) in the question or the answer; an empty search keeps all. */
export function filterFaqs(faqs: Faq[], search: string): Faq[] {
  const needle = search.trim().toLocaleLowerCase('de');
  if (!needle) {
    return faqs;
  }
  return faqs.filter((f) => f.title.toLocaleLowerCase('de').includes(needle) || f.description.toLocaleLowerCase('de').includes(needle));
}

/** A copy of the list with the entry at `index` moved by `delta` places (up = -1); unchanged at the ends. */
export function move<T>(items: T[], index: number, delta: number): T[] {
  const target = index + delta;
  if (index < 0 || index >= items.length || target < 0 || target >= items.length) {
    return items;
  }
  const copy = [...items];
  [copy[index], copy[target]] = [copy[target], copy[index]];
  return copy;
}
