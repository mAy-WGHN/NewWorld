/** A node of the rich-text document (TipTap/ProseMirror JSON); the portal validates the same contract. */
export interface FaqDoc {
  type: string;
  attrs?: Record<string, unknown>;
  content?: FaqDoc[];
  marks?: { type: string; attrs?: Record<string, unknown> }[];
  text?: string;
}

export const emptyDoc = (): FaqDoc => ({ type: 'doc', content: [{ type: 'paragraph' }] });

/** True if the document has text, an image or a table; an empty paragraph (or a list of them) is no content. */
export function docHasContent(doc: FaqDoc | null | undefined): boolean {
  if (!doc || typeof doc !== 'object') {
    return false;
  }
  if (doc.type === 'image' || doc.type === 'table') {
    return true;
  }
  if (doc.type === 'text') {
    return (doc.text ?? '').trim().length > 0;
  }
  return Array.isArray(doc.content) && doc.content.some(docHasContent);
}

const BLOCK_TYPES = new Set(['paragraph', 'heading', 'listItem', 'blockquote', 'tableRow', 'tableCell', 'tableHeader', 'bulletList', 'orderedList', 'table']);

/** The plain text of a document (for the search); blocks are separated by a space. */
export function docToText(doc: FaqDoc | null | undefined): string {
  const parts: string[] = [];
  const walk = (node: FaqDoc | null | undefined, depth: number): void => {
    if (!node || typeof node !== 'object' || depth > 40) {
      return;
    }
    if (node.type === 'text') {
      parts.push(node.text ?? '');
    } else if (node.type === 'hardBreak') {
      parts.push(' ');
    } else if (node.type === 'image') {
      parts.push(typeof node.attrs?.['alt'] === 'string' ? (node.attrs['alt'] as string) : '', ' ');
    }
    if (Array.isArray(node.content)) {
      node.content.forEach((child) => walk(child, depth + 1));
    }
    if (BLOCK_TYPES.has(node.type)) {
      parts.push(' ');
    }
  };
  walk(doc, 0);
  return parts.join('').replace(/\s+/g, ' ').trim();
}

export interface Preset {
  label: string;
  value: string;
}

/** Font sizes of the toolbar; "Normal" (16) means no size at all. */
export const FONT_SIZES: Preset[] = [
  { label: 'Klein (12)', value: '12px' },
  { label: 'Etwas klein (14)', value: '14px' },
  { label: 'Normal (16)', value: '' },
  { label: 'Etwas groß (18)', value: '18px' },
  { label: 'Groß (20)', value: '20px' },
  { label: 'Größer (24)', value: '24px' },
  { label: 'Sehr groß (28)', value: '28px' },
  { label: 'Riesig (32)', value: '32px' },
];
export const ALLOWED_FONT_SIZES: readonly string[] = ['12px', '14px', '16px', '18px', '20px', '24px', '28px', '32px'];

/** Text colours: dark enough on a light and bright enough on a dark background. */
export const TEXT_COLORS: Preset[] = [
  { label: 'Rot', value: '#d93025' },
  { label: 'Orange', value: '#e8710a' },
  { label: 'Gelb', value: '#b58900' },
  { label: 'Grün', value: '#188038' },
  { label: 'Türkis', value: '#12a4af' },
  { label: 'Blau', value: '#1a73e8' },
  { label: 'Violett', value: '#9334e6' },
  { label: 'Grau', value: '#80868b' },
];

/** Highlight colours: light, so that dark and light text stays readable on them. */
export const HIGHLIGHT_COLORS: Preset[] = [
  { label: 'Gelb', value: '#fff176' },
  { label: 'Orange', value: '#ffcc80' },
  { label: 'Rot', value: '#ef9a9a' },
  { label: 'Grün', value: '#a5d6a7' },
  { label: 'Türkis', value: '#80deea' },
  { label: 'Blau', value: '#90caf9' },
  { label: 'Violett', value: '#ce93d8' },
  { label: 'Grau', value: '#cfd8dc' },
];

const HEX_COLOR = /^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/;
const OWN_IMAGE = /^(?:https?:\/\/[^/?#\s]+)?\/api\/faqs\/images\/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/** The colour if it is `#rgb` or `#rrggbb`, else null. */
export const safeColor = (value: unknown): string | null => (typeof value === 'string' && HEX_COLOR.test(value) ? value : null);

export const safeFontSize = (value: unknown): string | null => (typeof value === 'string' && ALLOWED_FONT_SIZES.includes(value) ? value : null);

export const safeAlign = (value: unknown): 'left' | 'center' | 'right' | 'justify' | null =>
  value === 'left' || value === 'center' || value === 'right' || value === 'justify' ? value : null;

/** Links only to web or mail addresses. */
export const safeHref = (value: unknown): string | null => (typeof value === 'string' && /^(?:https?:\/\/\S+|mailto:\S+)$/i.test(value.trim()) ? value.trim() : null);

/** Images only from the portal itself (`/api/faqs/images/{guid}`, absolute as the upload returns it or relative). */
export const safeImageSrc = (value: unknown): string | null => (typeof value === 'string' && OWN_IMAGE.test(value) ? value : null);

/** An FAQ entry of the portal (`GET /api/faqs`). The answer is `content`; `description` is derived text for search and old clients. */
export interface Faq {
  id: string;
  title: string;
  /** The answer as rich-text document. */
  content: FaqDoc;
  /** Derived plain text, only for the search; never shown as the answer and never sent. */
  description?: string;
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
  return faqs.filter((f) => f.title.toLocaleLowerCase('de').includes(needle) || (f.content ? docToText(f.content) : (f.description ?? '')).toLocaleLowerCase('de').includes(needle));
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

/** A copy of the document without empty attributes (`null`), as the portal's contract wants it (an attribute is there or it is not). */
export function stripNulls(node: FaqDoc): FaqDoc {
  const clean = (attrs: Record<string, unknown> | undefined): Record<string, unknown> | undefined => {
    const kept = Object.entries(attrs ?? {}).filter(([, v]) => v !== null && v !== undefined);
    return kept.length > 0 ? Object.fromEntries(kept) : undefined;
  };
  const { attrs, content, marks, ...rest } = node;
  const result: FaqDoc = { ...rest };
  const cleanAttrs = clean(attrs);
  if (cleanAttrs) {
    result.attrs = cleanAttrs;
  }
  if (content) {
    result.content = content.map(stripNulls);
  }
  if (marks) {
    result.marks = marks.map((m) => {
      const markAttrs = clean(m.attrs);
      return markAttrs ? { type: m.type, attrs: markAttrs } : { type: m.type };
    });
  }
  return result;
}
