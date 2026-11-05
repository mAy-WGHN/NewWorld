import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FaqContentView } from './faq-content-view';
import { FaqDoc } from './faq-models';

const ID = '3f2b8c1e-1111-4222-8333-944455556666';
const text = (value: string, marks?: FaqDoc['marks']): FaqDoc => ({ type: 'text', text: value, ...(marks ? { marks } : {}) });
const para = (...inline: FaqDoc[]): FaqDoc => ({ type: 'paragraph', content: inline });
const doc = (...blocks: FaqDoc[]): FaqDoc => ({ type: 'doc', content: blocks });

describe('FaqContentView', () => {
  let fixture: ComponentFixture<FaqContentView>;
  let root: HTMLElement;

  async function show(content: FaqDoc | null): Promise<void> {
    fixture = TestBed.createComponent(FaqContentView);
    fixture.componentRef.setInput('content', content);
    await fixture.whenStable();
    root = fixture.nativeElement as HTMLElement;
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);

  it('renders paragraphs and headings with their alignment', async () => {
    await show(
      doc(
        { type: 'heading', attrs: { level: 1 }, content: [text('Eins')] },
        { type: 'heading', attrs: { level: 2, textAlign: 'center' }, content: [text('Zwei')] },
        { type: 'heading', attrs: { level: 3 }, content: [text('Drei')] },
        { type: 'paragraph', attrs: { textAlign: 'justify' }, content: [text('Absatz'), { type: 'hardBreak' }, text('Neue Zeile')] },
      ),
    );

    expect(q('h1')!.textContent).toBe('Eins');
    expect(q<HTMLElement>('h2')!.style.textAlign).toBe('center');
    expect(q('h3')!.textContent).toBe('Drei');
    expect(q<HTMLElement>('p')!.style.textAlign).toBe('justify');
    expect(q('p br')).not.toBeNull();
  });

  it('renders bold, italic, underline and strike, also nested', async () => {
    await show(doc(para(text('a', [{ type: 'bold' }]), text('b', [{ type: 'italic' }]), text('c', [{ type: 'underline' }]), text('d', [{ type: 'strike' }]), text('e', [{ type: 'bold' }, { type: 'italic' }]))));

    expect(q('strong')!.textContent).toBe('a');
    expect(q('em')!.textContent).toBe('b');
    expect(q('u')!.textContent).toBe('c');
    expect(q('s')!.textContent).toBe('d');
    expect(q('strong em')!.textContent).toBe('e');
  });

  it('renders web and mail links that open in a new tab without the opener', async () => {
    await show(
      doc(para(text('web', [{ type: 'link', attrs: { href: 'https://example.com/x' } }]), text('mail', [{ type: 'link', attrs: { href: 'mailto:hr@example.com' } }]))),
    );

    const links = root.querySelectorAll('a');
    expect([...links].map((a) => a.getAttribute('href'))).toEqual(['https://example.com/x', 'mailto:hr@example.com']);
    expect(links[0].getAttribute('target')).toBe('_blank');
    expect(links[0].getAttribute('rel')).toBe('noopener noreferrer');
  });

  it('shows the text of links that are not web or mail addresses, without a link', async () => {
    await show(
      doc(para(text('js', [{ type: 'link', attrs: { href: 'javascript:alert(1)' } }]), text('data', [{ type: 'link', attrs: { href: 'data:text/html,x' } }]), text('none', [{ type: 'link', attrs: {} }]))),
    );

    expect(q('a')).toBeNull();
    expect(q('p')!.textContent).toBe('jsdatanone');
  });

  it('applies allowed colours, font sizes and highlights', async () => {
    await show(
      doc(
        para(
          text('rot', [{ type: 'textStyle', attrs: { color: '#d93025', fontSize: '24px' } }]),
          text('gelb', [{ type: 'highlight', attrs: { color: '#fff' } }]),
        ),
      ),
    );

    const span = q<HTMLElement>('span')!;
    expect(span.style.color).not.toBe('');
    expect(span.style.fontSize).toBe('24px');
    expect(q<HTMLElement>('mark')!.style.backgroundColor).not.toBe('');
    expect(q('mark')!.textContent).toBe('gelb');
  });

  it('ignores colours, sizes and alignments outside the allowed values', async () => {
    await show(
      doc(
        {
          type: 'paragraph',
          attrs: { textAlign: 'left; background:url(http://evil)' },
          content: [
            text('a', [{ type: 'textStyle', attrs: { color: 'red', fontSize: '99px' } }]),
            text('b', [{ type: 'textStyle', attrs: { color: 'url(http://evil.example/x)', fontSize: '16px; position:fixed' } }]),
            text('c', [{ type: 'highlight', attrs: { color: 'expression(alert(1))' } }]),
          ],
        },
      ),
    );

    expect(q('span')).toBeNull();
    expect(q<HTMLElement>('p')!.style.textAlign).toBe('');
    expect(q<HTMLElement>('mark')!.style.backgroundColor).toBe('');
    expect(root.innerHTML).not.toContain('evil');
    expect(q('p')!.textContent).toBe('abc');
  });

  it('renders bullet and ordered lists, the numbered list from its start', async () => {
    const item = (value: string): FaqDoc => ({ type: 'listItem', content: [para(text(value))] });
    await show(
      doc(
        { type: 'bulletList', content: [item('eins'), item('zwei')] },
        { type: 'orderedList', attrs: { start: 4 }, content: [item('vier'), item('fünf')] },
        { type: 'orderedList', content: [item('a')] },
      ),
    );

    expect(root.querySelectorAll('ul > li')).toHaveLength(2);
    const lists = root.querySelectorAll('ol');
    expect(lists[0].getAttribute('start')).toBe('4');
    expect(lists[0].querySelectorAll('li')).toHaveLength(2);
    expect(lists[1].hasAttribute('start')).toBe(false);
  });

  it('renders blockquotes and horizontal rules', async () => {
    await show(doc({ type: 'blockquote', content: [para(text('Zitat'))] }, { type: 'horizontalRule' }));

    expect(q('blockquote p')!.textContent).toBe('Zitat');
    expect(q('hr')).not.toBeNull();
  });

  it('renders tables with header cells inside a scrollable wrapper', async () => {
    const cell = (type: string, value: string, attrs?: Record<string, unknown>): FaqDoc => ({ type, attrs, content: [para(text(value))] });
    await show(
      doc({
        type: 'table',
        content: [
          { type: 'tableRow', content: [cell('tableHeader', 'Kopf', { colspan: 2 }), cell('tableHeader', 'K2')] },
          { type: 'tableRow', content: [cell('tableCell', 'a'), cell('tableCell', 'b', { rowspan: 2, colspan: 1 })] },
        ],
      }),
    );

    expect(q('.tableWrapper > table')).not.toBeNull();
    expect(root.querySelectorAll('tr')).toHaveLength(2);
    expect(root.querySelectorAll('th')).toHaveLength(2);
    expect(q('th')!.getAttribute('colspan')).toBe('2');
    expect(root.querySelectorAll('td')[1].getAttribute('rowspan')).toBe('2');
    expect(root.querySelectorAll('td')[1].hasAttribute('colspan')).toBe(false);
  });

  it('renders images of the portal only (absolute or relative address), with alt text and lazy loading', async () => {
    await show(
      doc(
        { type: 'image', attrs: { src: `https://portal.example/api/faqs/images/${ID}`, alt: 'Formular' } },
        { type: 'image', attrs: { src: `/api/faqs/images/${ID}` } },
      ),
    );

    const images = root.querySelectorAll('img');
    expect(images).toHaveLength(2);
    expect(images[0].getAttribute('alt')).toBe('Formular');
    expect(images[0].getAttribute('loading')).toBe('lazy');
    expect(images[1].getAttribute('src')).toBe(`/api/faqs/images/${ID}`);
  });

  it('refuses images from anywhere else', async () => {
    await show(
      doc(
        { type: 'image', attrs: { src: 'https://evil.example/pixel.png' } },
        { type: 'image', attrs: { src: `https://evil.example/x/api/faqs/images/${ID}/../y` } },
        { type: 'image', attrs: { src: 'data:image/png;base64,AAAA' } },
        { type: 'image', attrs: { src: 'javascript:alert(1)' } },
        { type: 'image', attrs: { src: `/api/faqs/images/${ID}?x=1` } },
        { type: 'image', attrs: {} },
      ),
    );

    expect(q('img')).toBeNull();
  });

  it('renders nothing for unknown nodes, also with content inside', async () => {
    await show(doc({ type: 'script', content: [para(text('böse'))] }, { type: 'codeBlock', content: [text('x')] }, para(text('ok'))));

    expect(root.textContent).toBe('ok');
    expect(q('script')).toBeNull();
  });

  it('leaves out nodes in places where they do not belong', async () => {
    await show(doc({ type: 'bulletList', content: [para(text('kein Listenpunkt'))] }, { type: 'paragraph', content: [para(text('Absatz im Absatz'))] }));

    expect(root.textContent).toBe('');
  });

  it('never uses innerHTML: markup in the text stays text', async () => {
    const attack = '<script>alert(1)</script><img src=x onerror=alert(1)>';
    await show(doc(para(text(attack, [{ type: 'bold' }])), { type: 'heading', attrs: { level: 1 }, content: [text(attack)] }));

    expect(q('script')).toBeNull();
    expect(q('img')).toBeNull();
    expect(q('strong')!.textContent).toBe(attack);
    expect(q('h1')!.textContent).toBe(attack);
  });

  it('shows nothing for an empty, missing or malformed document', async () => {
    await show(null);
    expect(root.textContent!.trim()).toBe('');
    await show({ type: 'paragraph' } as FaqDoc);
    expect(root.textContent!.trim()).toBe('');
    await show({ type: 'doc', content: 'text' as unknown as FaqDoc[] });
    expect(root.textContent!.trim()).toBe('');
  });

  it('stops at an absurd nesting depth instead of overflowing', async () => {
    let inner: FaqDoc = para(text('tief'));
    for (let i = 0; i < 500; i++) {
      inner = { type: 'blockquote', content: [inner] };
    }
    await show(doc(inner));

    expect(root.textContent).not.toContain('tief');
  });
});
