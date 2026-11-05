/**
 * A small, safe reader for the Markdown of FAQ answers. It does not produce HTML: it produces typed blocks that the template renders
 * with normal Angular bindings, so nothing the author typed can ever become markup. Supported: headings (# to ###), paragraphs, bullet and
 * numbered lists, **bold**, *italic*, [links](https://…) and ![images](…/api/faqs/images/{id}).
 */
export type Inline =
  | { kind: 'text'; text: string }
  | { kind: 'bold'; text: string }
  | { kind: 'italic'; text: string }
  | { kind: 'link'; text: string; href: string }
  | { kind: 'image'; alt: string; src: string };

export type Block =
  | { kind: 'heading'; level: 1 | 2 | 3; inlines: Inline[] }
  | { kind: 'paragraph'; inlines: Inline[] }
  | { kind: 'list'; ordered: boolean; items: Inline[][] };

const IMAGE_PATH = /^(https?:\/\/[^\s/]+)?\/api\/faqs\/images\/[0-9a-fA-F-]{36}$/;
const INLINE = /!\[([^\]]*)\]\(([^)\s]+)\)|\[([^\]]+)\]\(([^)\s]+)\)|\*\*([^*]+)\*\*|\*([^*\s][^*]*)\*/g;

/** Only images of the FAQ itself (never other addresses, so a text cannot make the browser load something from elsewhere). */
export const safeImageSrc = (url: string): string | null => (IMAGE_PATH.test(url) ? url : null);

/** Only web and mail links; no `javascript:` and the like. */
export const safeHref = (url: string): string | null => (/^(https?:\/\/|mailto:)[^\s]+$/i.test(url) ? url : null);

export function parseInlines(text: string): Inline[] {
  const result: Inline[] = [];
  let last = 0;
  const push = (value: string) => {
    if (value) {
      result.push({ kind: 'text', text: value });
    }
  };
  for (const match of text.matchAll(INLINE)) {
    push(text.slice(last, match.index));
    last = match.index + match[0].length;
    if (match[2] !== undefined) {
      const src = safeImageSrc(match[2]);
      if (src) {
        result.push({ kind: 'image', alt: match[1], src });
      } else {
        push(match[0]);
      }
    } else if (match[4] !== undefined) {
      const href = safeHref(match[4]);
      if (href) {
        result.push({ kind: 'link', text: match[3], href });
      } else {
        push(match[3]);
      }
    } else if (match[5] !== undefined) {
      result.push({ kind: 'bold', text: match[5] });
    } else {
      result.push({ kind: 'italic', text: match[6] });
    }
  }
  push(text.slice(last));
  return result;
}

export function parseMarkdown(markdown: string): Block[] {
  const blocks: Block[] = [];
  let paragraph: string[] = [];
  let list: { ordered: boolean; items: Inline[][] } | null = null;

  const flushParagraph = () => {
    if (paragraph.length) {
      blocks.push({ kind: 'paragraph', inlines: parseInlines(paragraph.join(' ')) });
      paragraph = [];
    }
  };
  const flushList = () => {
    if (list) {
      blocks.push({ kind: 'list', ...list });
      list = null;
    }
  };

  for (const raw of markdown.replace(/\r\n?/g, '\n').split('\n')) {
    const line = raw.trim();
    const heading = /^(#{1,3})\s+(.+)$/.exec(line);
    const item = /^(?:([-*+])|(\d+)[.)])\s+(.+)$/.exec(line);
    if (!line) {
      flushParagraph();
      flushList();
    } else if (heading) {
      flushParagraph();
      flushList();
      blocks.push({ kind: 'heading', level: heading[1].length as 1 | 2 | 3, inlines: parseInlines(heading[2]) });
    } else if (item) {
      flushParagraph();
      const ordered = item[2] !== undefined;
      if (list && list.ordered !== ordered) {
        flushList();
      }
      list ??= { ordered, items: [] };
      list.items.push(parseInlines(item[3]));
    } else {
      flushList();
      paragraph.push(line);
    }
  }
  flushParagraph();
  flushList();
  return blocks;
}
