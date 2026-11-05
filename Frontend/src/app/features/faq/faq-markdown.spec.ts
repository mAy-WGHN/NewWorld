import { parseInlines, parseMarkdown, safeHref, safeImageSrc } from './faq-markdown';
import { filterFaqs, imageProblem, move } from './faq-models';

const ID = '3f2b8c1e-1111-4222-8333-944455556666';

describe('faq-markdown', () => {
  it('reads paragraphs, headings and lists', () => {
    const blocks = parseMarkdown('# Titel\n\nErste Zeile\nzweite Zeile\n\n- eins\n- zwei\n\n1. a\n2. b');

    expect(blocks.map((b) => b.kind)).toEqual(['heading', 'paragraph', 'list', 'list']);
    expect(blocks[1]).toEqual({ kind: 'paragraph', inlines: [{ kind: 'text', text: 'Erste Zeile zweite Zeile' }] });
    expect(blocks[2]).toMatchObject({ ordered: false });
    expect((blocks[2] as { items: unknown[] }).items).toHaveLength(2);
    expect(blocks[3]).toMatchObject({ ordered: true });
  });

  it('reads bold, italic and links', () => {
    expect(parseInlines('a **fett** und *schräg* und [hier](https://example.com/x)')).toEqual([
      { kind: 'text', text: 'a ' },
      { kind: 'bold', text: 'fett' },
      { kind: 'text', text: ' und ' },
      { kind: 'italic', text: 'schräg' },
      { kind: 'text', text: ' und ' },
      { kind: 'link', text: 'hier', href: 'https://example.com/x' },
    ]);
  });

  it('accepts images of the FAQ, with the absolute address the portal returns or a relative one', () => {
    expect(parseInlines(`![Bild](https://portal.example/api/faqs/images/${ID})`)).toEqual([
      { kind: 'image', alt: 'Bild', src: `https://portal.example/api/faqs/images/${ID}` },
    ]);
    expect(safeImageSrc(`/api/faqs/images/${ID}`)).toBe(`/api/faqs/images/${ID}`);
  });

  it('does not load images from anywhere else', () => {
    expect(safeImageSrc('https://evil.example/pixel.png')).toBeNull();
    expect(safeImageSrc(`https://evil.example/x/api/faqs/images/${ID}/../y`)).toBeNull();
    expect(parseInlines('![x](https://evil.example/pixel.png)').every((p) => p.kind !== 'image')).toBe(true);
  });

  it('drops links that are not web or mail addresses and keeps their text', () => {
    expect(safeHref('javascript:alert(1)')).toBeNull();
    expect(safeHref('data:text/html,x')).toBeNull();
    expect(parseInlines('[klick](javascript:void)')).toEqual([{ kind: 'text', text: 'klick' }]);
    expect(safeHref('mailto:hr@example.com')).toBe('mailto:hr@example.com');
  });

  it('keeps markup as plain text (never HTML)', () => {
    const blocks = parseMarkdown('<script>alert(1)</script> <img src=x onerror=alert(1)>');

    expect(blocks).toEqual([{ kind: 'paragraph', inlines: [{ kind: 'text', text: '<script>alert(1)</script> <img src=x onerror=alert(1)>' }] }]);
  });

  it('copes with empty text and Windows line breaks', () => {
    expect(parseMarkdown('')).toEqual([]);
    expect(parseMarkdown('a\r\n\r\nb')).toHaveLength(2);
  });
});

describe('faq-models', () => {
  const faq = (title: string, description: string) => ({ id: title, title, description, order: 0, archived: false, createdAt: '' });

  it('searches question and answer, ignoring case', () => {
    const list = [faq('Urlaub beantragen', 'Im Portal'), faq('Krankmeldung', 'Bitte ÜBER das Portal')];

    expect(filterFaqs(list, 'urlaub').map((f) => f.title)).toEqual(['Urlaub beantragen']);
    expect(filterFaqs(list, 'über').map((f) => f.title)).toEqual(['Krankmeldung']);
    expect(filterFaqs(list, 'portal')).toHaveLength(2);
    expect(filterFaqs(list, 'nichts')).toEqual([]);
    expect(filterFaqs(list, '  ')).toBe(list);
  });

  it('moves an entry and leaves the list at the ends', () => {
    expect(move(['a', 'b', 'c'], 1, -1)).toEqual(['b', 'a', 'c']);
    expect(move(['a', 'b', 'c'], 1, 1)).toEqual(['a', 'c', 'b']);
    const list = ['a', 'b'];
    expect(move(list, 0, -1)).toBe(list);
    expect(move(list, 1, 1)).toBe(list);
  });

  it('refuses images of other types and over 5 MB', () => {
    expect(imageProblem({ type: 'image/png', size: 1000 })).toBeNull();
    expect(imageProblem({ type: 'image/webp', size: 5 * 1024 * 1024 })).toBeNull();
    expect(imageProblem({ type: 'application/pdf', size: 1000 })).toContain('PNG');
    expect(imageProblem({ type: 'image/gif', size: 1000 })).toContain('PNG');
    expect(imageProblem({ type: 'image/png', size: 5 * 1024 * 1024 + 1 })).toContain('zu groß');
  });
});
