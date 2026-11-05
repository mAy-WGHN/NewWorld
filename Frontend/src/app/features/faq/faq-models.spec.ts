import { FaqDoc, docHasContent, docToText, emptyDoc, filterFaqs, imageProblem, move, safeAlign, safeColor, safeFontSize, safeHref, safeImageSrc } from './faq-models';

const ID = '3f2b8c1e-1111-4222-8333-944455556666';
const text = (value: string): FaqDoc => ({ type: 'text', text: value });
const para = (...inline: FaqDoc[]): FaqDoc => ({ type: 'paragraph', content: inline });
const doc = (...blocks: FaqDoc[]): FaqDoc => ({ type: 'doc', content: blocks });

describe('faq-models: document', () => {
  it('knows the empty document has no content', () => {
    expect(docHasContent(emptyDoc())).toBe(false);
    expect(docHasContent(doc(para(), para(text('   '))))).toBe(false);
    expect(docHasContent(doc({ type: 'bulletList', content: [{ type: 'listItem', content: [para()] }] }))).toBe(false);
    expect(docHasContent(null)).toBe(false);
    expect(docHasContent(undefined)).toBe(false);
  });

  it('sees text, images and tables as content', () => {
    expect(docHasContent(doc(para(text('Hallo'))))).toBe(true);
    expect(docHasContent(doc({ type: 'image', attrs: { src: `/api/faqs/images/${ID}` } }))).toBe(true);
    expect(docHasContent(doc({ type: 'table', content: [] }))).toBe(true);
    expect(docHasContent(doc({ type: 'bulletList', content: [{ type: 'listItem', content: [para(text('x'))] }] }))).toBe(true);
  });

  it('turns a document into plain text with blocks apart', () => {
    const d = doc(
      { type: 'heading', attrs: { level: 1 }, content: [text('Titel')] },
      para(text('Eins'), { type: 'hardBreak' }, text('zwei')),
      { type: 'bulletList', content: [{ type: 'listItem', content: [para(text('Punkt'))] }] },
      { type: 'image', attrs: { alt: 'Bild' } },
    );

    expect(docToText(d)).toBe('Titel Eins zwei Punkt Bild');
    expect(docToText(null)).toBe('');
    expect(docToText(emptyDoc())).toBe('');
  });
});

describe('faq-models: safe values', () => {
  it('accepts hex colours only', () => {
    expect(safeColor('#fff')).toBe('#fff');
    expect(safeColor('#D93025')).toBe('#D93025');
    for (const bad of ['red', '#ff', '#ffff', '#gggggg', 'rgb(0,0,0)', 'url(x)', '#fff;x', 1, null]) {
      expect(safeColor(bad)).toBeNull();
    }
  });

  it('accepts the listed font sizes and alignments only', () => {
    expect(safeFontSize('12px')).toBe('12px');
    expect(safeFontSize('32px')).toBe('32px');
    expect(safeFontSize('33px')).toBeNull();
    expect(safeFontSize('16px; x')).toBeNull();
    expect(safeAlign('justify')).toBe('justify');
    expect(safeAlign('start')).toBeNull();
  });

  it('accepts http, https and mailto links only', () => {
    expect(safeHref('https://example.com')).toBe('https://example.com');
    expect(safeHref('mailto:hr@example.com')).toBe('mailto:hr@example.com');
    expect(safeHref('javascript:alert(1)')).toBeNull();
    expect(safeHref('data:text/html,x')).toBeNull();
    expect(safeHref('/relativ')).toBeNull();
    expect(safeHref('https://a b')).toBeNull();
  });

  it('accepts only the own images of the portal', () => {
    expect(safeImageSrc(`/api/faqs/images/${ID}`)).toBe(`/api/faqs/images/${ID}`);
    expect(safeImageSrc(`https://portal.example/api/faqs/images/${ID}`)).toBe(`https://portal.example/api/faqs/images/${ID}`);
    expect(safeImageSrc('https://evil.example/pixel.png')).toBeNull();
    expect(safeImageSrc(`https://evil.example/x/api/faqs/images/${ID}/../y`)).toBeNull();
    expect(safeImageSrc(`/api/faqs/images/${ID}/x`)).toBeNull();
  });
});

describe('faq-models: list', () => {
  const faq = (title: string, content: FaqDoc, description?: string) => ({ id: title, title, content, description, order: 0, archived: false, createdAt: '' });

  it('searches question and answer text, ignoring case and formatting', () => {
    const list = [faq('Urlaub beantragen', doc(para(text('Im Portal')))), faq('Krankmeldung', doc(para({ type: 'text', text: 'Bitte ÜBER das Portal', marks: [{ type: 'bold' }] })))];

    expect(filterFaqs(list, 'urlaub').map((f) => f.title)).toEqual(['Urlaub beantragen']);
    expect(filterFaqs(list, 'über').map((f) => f.title)).toEqual(['Krankmeldung']);
    expect(filterFaqs(list, 'portal')).toHaveLength(2);
    expect(filterFaqs(list, 'nichts')).toEqual([]);
    expect(filterFaqs(list, '  ')).toBe(list);
  });

  it('falls back to the derived text if an entry has no document', () => {
    const old = { ...faq('Alt', undefined as unknown as FaqDoc), description: 'Nur Text' };

    expect(filterFaqs([old], 'nur text')).toHaveLength(1);
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
