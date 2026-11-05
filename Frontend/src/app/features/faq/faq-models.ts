/** An FAQ entry of the portal (`GET /api/faqs`). The answer (`description`) is Markdown. */
export interface Faq {
  id: string;
  title: string;
  description: string;
  order: number;
  archived: boolean;
  createdAt: string;
  /** Only filled for signed-in people (the entry itself is public). */
  attachments?: FaqAttachment[];
}

/** A file attached to an entry (PDF, image, Office document); `url` is the path to fetch it (signed-in people only). */
export interface FaqAttachment {
  id: string;
  fileName: string;
  contentType: string;
  length: number;
  isImage: boolean;
  url: string;
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

export const MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;
export const MAX_ATTACHMENTS = 20;
const ATTACHMENT_EXTENSIONS = ['pdf', 'png', 'jpg', 'jpeg', 'webp', 'docx', 'xlsx', 'pptx', 'doc', 'xls', 'ppt'];
export const ATTACHMENT_ACCEPT = ATTACHMENT_EXTENSIONS.map((e) => '.' + e).join(',');

/** Why a file cannot be attached (the portal checks the content again), or null if it can. */
export function attachmentProblem(file: { name: string; size: number }): string | null {
  const dot = file.name.lastIndexOf('.');
  const extension = dot < 0 ? '' : file.name.slice(dot + 1).toLowerCase();
  if (!ATTACHMENT_EXTENSIONS.includes(extension)) {
    return 'Erlaubt sind PDF, Bilder (PNG, JPEG, WebP) und Office-Dokumente (Word, Excel, PowerPoint).';
  }
  if (file.size === 0 || file.size > MAX_ATTACHMENT_BYTES) {
    return 'Die Datei ist leer oder zu groß. Erlaubt sind höchstens 10 MB.';
  }
  return null;
}

/** PDF and images open in a tab; Office documents are downloaded. */
export const opensInTab = (a: Pick<FaqAttachment, 'isImage' | 'contentType'>): boolean => a.isImage || a.contentType === 'application/pdf';

/** The Material icon for a file. */
export function attachmentIcon(a: Pick<FaqAttachment, 'isImage' | 'contentType'>): string {
  if (a.isImage) {
    return 'image';
  }
  if (a.contentType === 'application/pdf') {
    return 'picture_as_pdf';
  }
  if (a.contentType.includes('spreadsheet') || a.contentType.includes('excel')) {
    return 'table_chart';
  }
  if (a.contentType.includes('presentation') || a.contentType.includes('powerpoint')) {
    return 'slideshow';
  }
  return 'description';
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
