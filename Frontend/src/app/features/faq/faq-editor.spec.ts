import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FaqEditor } from './faq-editor';
import { FaqDoc } from './faq-models';

const ID = '3f2b8c1e-1111-4222-8333-944455556666';
const text = (value: string): FaqDoc => ({ type: 'text', text: value });
const para = (...inline: FaqDoc[]): FaqDoc => ({ type: 'paragraph', content: inline });
const doc = (...blocks: FaqDoc[]): FaqDoc => ({ type: 'doc', content: blocks });

describe('FaqEditor', () => {
  let fixture: ComponentFixture<FaqEditor>;
  let component: FaqEditor;
  let root: HTMLElement;
  let emitted: FaqDoc[];

  async function settle(): Promise<void> {
    await fixture.whenStable();
    await new Promise((r) => setTimeout(r));
    await fixture.whenStable();
  }

  async function create(content: FaqDoc = doc(para(text('Hallo Welt'))), disabled = false): Promise<void> {
    fixture = TestBed.createComponent(FaqEditor);
    component = fixture.componentInstance;
    root = fixture.nativeElement as HTMLElement;
    emitted = [];
    component.changed.subscribe((d) => emitted.push(d));
    fixture.componentRef.setInput('content', content);
    fixture.componentRef.setInput('disabled', disabled);
    await settle();
  }

  const q = <T extends Element = HTMLElement>(selector: string): T | null => root.querySelector<T>(selector);
  const button = (action: string) => q<HTMLButtonElement>(`[data-action="${action}"]`)!;
  const json = () => component.getDoc();
  const selectAll = () => component.editor.commands.selectAll();

  async function click(action: string): Promise<void> {
    button(action).click();
    await settle();
  }

  async function choose(action: string, value: string): Promise<void> {
    const select = q<HTMLSelectElement>(`[data-action="${action}"]`)!;
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    await settle();
  }

  /** The marks of the first text of the first block. */
  const marksOf = (d: FaqDoc = json()) => d.content![0].content![0].marks ?? [];

  it('shows the start document in the editing area', async () => {
    await create();

    expect(q('[data-testid="editor-surface"]')!.textContent).toBe('Hallo Welt');
    expect(q('[data-testid="editor-surface"]')!.getAttribute('aria-label')).toBe('Antwort');
    expect(emitted).toEqual([]); // taking over the start document is not a change
  });

  it('has real buttons with names and an active state', async () => {
    await create();

    const buttons = [...root.querySelectorAll<HTMLButtonElement>('[data-testid="toolbar"] button')];
    expect(buttons.length).toBeGreaterThan(15);
    for (const b of buttons) {
      expect(b.type).toBe('button');
      expect(b.getAttribute('aria-label') || b.textContent!.trim()).toBeTruthy();
      expect(b.title || b.getAttribute('aria-label')).toBeTruthy();
    }
    selectAll();
    await click('bold');
    expect(button('bold').getAttribute('aria-pressed')).toBe('true');
    expect(button('italic').getAttribute('aria-pressed')).toBe('false');
  });

  it('makes text bold, italic, underlined and struck through, and emits the JSON', async () => {
    await create();
    selectAll();

    await click('bold');
    await click('italic');
    await click('underline');
    await click('strike');

    expect(marksOf().map((m) => m.type).sort()).toEqual(['bold', 'italic', 'strike', 'underline']);
    expect(emitted.length).toBeGreaterThanOrEqual(4);
    expect(marksOf(emitted.at(-1))).toHaveLength(4);

    await click('bold');
    expect(marksOf().map((m) => m.type)).not.toContain('bold');
  });

  it('turns the paragraph into a heading of level 1 to 3 and back', async () => {
    await create();

    await choose('block', '2');
    expect(json().content![0]).toMatchObject({ type: 'heading', attrs: { level: 2 } });
    await choose('block', '3');
    expect(json().content![0]).toMatchObject({ type: 'heading', attrs: { level: 3 } });
    await choose('block', 'p');
    expect(json().content![0].type).toBe('paragraph');
  });

  it('makes bullet and ordered lists', async () => {
    await create();

    await click('bullet-list');
    expect(json().content![0].type).toBe('bulletList');
    await click('bullet-list');
    expect(json().content![0].type).toBe('paragraph');
    await click('ordered-list');
    expect(json().content![0].type).toBe('orderedList');
  });

  it('makes a quote and a horizontal rule', async () => {
    await create();

    await click('blockquote');
    expect(json().content![0].type).toBe('blockquote');
    await click('rule');
    expect(JSON.stringify(json())).toContain('horizontalRule');
  });

  it('sets the font size and removes it for "Normal"', async () => {
    await create();
    selectAll();

    await choose('font-size', '24px');
    expect(marksOf()).toEqual([{ type: 'textStyle', attrs: { fontSize: '24px' } }]);
    await choose('font-size', '');
    expect(marksOf()).toEqual([]);
  });

  it('ignores a font size that is not offered', async () => {
    await create();
    selectAll();

    await choose('font-size', '99px');

    expect(marksOf()).toEqual([]);
  });

  it('sets and resets the text colour with the palette', async () => {
    await create();
    selectAll();

    await click('color-menu');
    q<HTMLButtonElement>('[data-action="set-color"][aria-label="Rot"]')!.click();
    await settle();
    expect(marksOf()).toEqual([{ type: 'textStyle', attrs: { color: '#d93025' } }]);
    expect(q('[data-testid="color-palette"]')).toBeNull(); // the palette closes after the choice

    await click('color-menu');
    await click('reset-color');
    expect(marksOf()).toEqual([]);
  });

  it('highlights and resets the highlight', async () => {
    await create();
    selectAll();

    await click('highlight-menu');
    q<HTMLButtonElement>('[data-action="set-highlight"][aria-label="Gelb"]')!.click();
    await settle();
    expect(marksOf()).toEqual([{ type: 'highlight', attrs: { color: '#fff176' } }]);

    await click('highlight-menu');
    await click('reset-highlight');
    expect(marksOf()).toEqual([]);
  });

  it('aligns the paragraph', async () => {
    await create();

    await click('align-center');
    expect(json().content![0].attrs).toMatchObject({ textAlign: 'center' });
    await click('align-right');
    expect(json().content![0].attrs).toMatchObject({ textAlign: 'right' });
    await click('align-justify');
    expect(json().content![0].attrs).toMatchObject({ textAlign: 'justify' });
    await click('align-left');
    expect(json().content![0].attrs).toMatchObject({ textAlign: 'left' });
  });

  async function link(address: string): Promise<void> {
    if (!q('[data-testid="link-form"]')) {
      await click('link-menu');
    }
    const input = q<HTMLInputElement>('#faq-link-address')!;
    input.value = address;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    q<HTMLFormElement>('[data-testid="link-form"]')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await settle();
  }

  it('makes a link from web and mail addresses; an address without scheme gets https', async () => {
    await create();
    selectAll();

    await link('example.com/x');
    expect(marksOf()).toEqual([{ type: 'link', attrs: expect.objectContaining({ href: 'https://example.com/x' }) }]);

    await link('mailto:hr@example.com');
    expect(marksOf()[0].attrs).toMatchObject({ href: 'mailto:hr@example.com' });
  });

  it('refuses links with other schemes and says why', async () => {
    await create();
    selectAll();

    for (const bad of ['javascript:alert(1)', 'data:text/html,x', 'ftp://example.com', 'JaVaScRiPt:alert(1)']) {
      await link(bad);
      expect(marksOf()).toEqual([]);
      expect(q('[data-testid="link-error"]')!.textContent).toContain('http');
    }
  });

  it('asks for a selection before it makes a link, and removes a link', async () => {
    await create();

    component.editor.commands.setTextSelection(3);
    await link('https://example.com');
    expect(marksOf()).toEqual([]);
    expect(q('[data-testid="link-error"]')!.textContent).toContain('Markiere');

    selectAll();
    await link('https://example.com');
    expect(marksOf()).toHaveLength(1);
    await click('link-menu');
    await click('remove-link');
    expect(marksOf()).toEqual([]);
  });

  it('does not open links while editing', async () => {
    await create(doc(para({ type: 'text', text: 'Link', marks: [{ type: 'link', attrs: { href: 'https://example.com' } }] } as FaqDoc)));
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);

    q('[data-testid="editor-surface"] a')!.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));

    expect(open).not.toHaveBeenCalled();
    open.mockRestore();
  });

  async function insertTable(rows = 2, cols = 3): Promise<void> {
    await click('table-menu');
    const set = (id: string, value: number) => {
      const input = q<HTMLInputElement>(id)!;
      input.value = String(value);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    };
    set('#faq-table-rows', rows);
    set('#faq-table-cols', cols);
    await settle();
    await click('insert-table');
  }

  const table = () => json().content!.find((n) => n.type === 'table')!;

  it('inserts a table with a header row', async () => {
    await create();

    await insertTable(2, 3);

    expect(table().content).toHaveLength(2);
    expect(table().content![0].content!.map((c) => c.type)).toEqual(['tableHeader', 'tableHeader', 'tableHeader']);
    expect(table().content![1].content!.map((c) => c.type)).toEqual(['tableCell', 'tableCell', 'tableCell']);
  });

  it('keeps the size of the table inside the limits', async () => {
    await create();

    await insertTable(500, 0);

    expect(table().content).toHaveLength(20);
    expect(table().content![0].content).toHaveLength(1);
  });

  it('shows table tools only inside a table and adds and deletes rows and columns, and the table', async () => {
    await create();
    expect(q('[data-testid="table-tools"]')).toBeNull();

    await insertTable(2, 2);
    expect(q('[data-testid="table-tools"]')).not.toBeNull();

    await click('add-row');
    expect(table().content).toHaveLength(3);
    await click('delete-row');
    expect(table().content).toHaveLength(2);
    await click('add-col');
    expect(table().content![0].content).toHaveLength(3);
    await click('delete-col');
    expect(table().content![0].content).toHaveLength(2);
    await click('delete-table');
    expect(json().content!.some((n) => n.type === 'table')).toBe(false);
    expect(q('[data-testid="table-tools"]')).toBeNull();
  });

  it('undoes, redoes and removes the formatting', async () => {
    await create();
    expect(button('undo').disabled).toBe(true);
    selectAll();
    await click('bold');
    await choose('font-size', '20px');

    await click('clear-format');
    expect(marksOf()).toEqual([]);

    await click('undo');
    expect(marksOf().length).toBeGreaterThan(0);
    expect(button('redo').disabled).toBe(false);
    await click('redo');
    expect(marksOf()).toEqual([]);
  });

  it('inserts an image of the portal as its own block, with alt text', async () => {
    await create();

    component.insertImage(`https://portal.example/api/faqs/images/${ID}`, 'Formular');
    await settle();

    expect(json().content!.find((n) => n.type === 'image')).toEqual({ type: 'image', attrs: { src: `https://portal.example/api/faqs/images/${ID}`, alt: 'Formular' } });
    expect(q('[data-testid="editor-surface"] img')).not.toBeNull();
    expect(emitted.at(-1)!.content!.some((n) => n.type === 'image')).toBe(true);
  });

  it('writes only the attributes of the contract for images', async () => {
    await create(doc(para(text('x')), { type: 'image', attrs: { src: `/api/faqs/images/${ID}`, alt: 'A', title: 'T', width: 10 } }));

    expect(json().content![1].attrs).toEqual({ src: `/api/faqs/images/${ID}`, alt: 'A' });
  });

  it('takes over another start document when the entry being edited changes', async () => {
    await create();

    fixture.componentRef.setInput('content', doc(para(text('Etwas anderes'))));
    await settle();

    expect(q('[data-testid="editor-surface"]')!.textContent).toBe('Etwas anderes');
    expect(emitted).toEqual([]);
  });

  it('does not write while disabled and writes again afterwards', async () => {
    await create(doc(para(text('Hallo'))), true);

    expect(component.editor.isEditable).toBe(false);
    for (const b of root.querySelectorAll<HTMLButtonElement>('[data-testid="toolbar"] button')) {
      expect(b.disabled).toBe(true);
    }
    expect(q<HTMLSelectElement>('[data-action="block"]')!.disabled).toBe(true);

    fixture.componentRef.setInput('disabled', false);
    await settle();
    expect(component.editor.isEditable).toBe(true);
    expect(button('bold').disabled).toBe(false);
  });

  it('destroys the editor with the component', async () => {
    await create();
    const editor = component.editor;

    fixture.destroy();

    expect(editor.isDestroyed).toBe(true);
  });
});
