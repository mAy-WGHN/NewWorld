import { Component, DestroyRef, ElementRef, afterNextRender, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Editor, Extension } from '@tiptap/core';
import { Highlight } from '@tiptap/extension-highlight';
import { Image } from '@tiptap/extension-image';
import { Table, TableCell, TableHeader, TableRow } from '@tiptap/extension-table';
import { TextAlign } from '@tiptap/extension-text-align';
import { TextStyleKit } from '@tiptap/extension-text-style';
import { StarterKit } from '@tiptap/starter-kit';
import { FONT_SIZES, FaqDoc, HIGHLIGHT_COLORS, Preset, TEXT_COLORS, emptyDoc, safeColor, safeFontSize, safeHref, stripNulls } from './faq-models';

type Palette = 'color' | 'highlight' | 'link' | 'table' | null;
type Chain = ReturnType<Editor['chain']>;
type Align = 'left' | 'center' | 'right' | 'justify';

/** An image with only the attributes of the contract (`src`, `alt`), as a block. */
const FaqImageNode = Image.extend({
  addAttributes() {
    return { src: { default: null }, alt: { default: null } };
  },
}).configure({ inline: false, allowBase64: false });

/** The extensions of the contract: only these nodes and marks can be written. */
export function faqExtensions(): Extension[] {
  return [
    StarterKit.configure({
      heading: { levels: [1, 2, 3] },
      code: false,
      codeBlock: false,
      link: {
        openOnClick: false,
        autolink: false,
        isAllowedUri: (url: string) => safeHref(url) !== null,
      },
    }),
    TextStyleKit.configure({ backgroundColor: false, fontFamily: false, lineHeight: false }),
    Highlight.configure({ multicolor: true }),
    TextAlign.configure({ types: ['heading', 'paragraph'], alignments: ['left', 'center', 'right', 'justify'] }),
    FaqImageNode,
    Table.configure({ resizable: false }),
    TableRow,
    TableHeader,
    TableCell,
  ] as unknown as Extension[];
}

/**
 * The editor for the rich-text answers of the FAQ: TipTap (core API) with a toolbar. It gives back the document as JSON on every change
 * (`changed`); `content` is only the start (and is taken over again when the entry being edited is replaced by another one).
 */
@Component({
  selector: 'app-faq-editor',
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './faq-editor.html',
})
export class FaqEditor {
  readonly content = input<FaqDoc>(emptyDoc());
  readonly disabled = input(false);
  readonly changed = output<FaqDoc>();

  protected readonly fontSizes = FONT_SIZES;
  protected readonly textColors = TEXT_COLORS;
  protected readonly highlightColors = HIGHLIGHT_COLORS;

  /** Counts every change of document or selection, so that the toolbar state is read again. */
  private readonly version = signal(0);
  protected readonly palette = signal<Palette>(null);
  protected readonly linkError = signal<string | null>(null);
  protected readonly tableRows = signal(3);
  protected readonly tableCols = signal(3);

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('surface');
  private readonly element = document.createElement('div');
  private applied: FaqDoc | null = null;

  /** The TipTap editor (exposed for tests and for the page). */
  readonly editor: Editor;

  constructor() {
    this.editor = new Editor({
      element: this.element,
      extensions: faqExtensions(),
      content: emptyDoc(),
      editorProps: {
        attributes: { class: 'faq-content', role: 'textbox', 'aria-multiline': 'true', 'aria-label': 'Antwort', 'data-testid': 'editor-surface' },
      },
      onTransaction: () => this.version.update((v) => v + 1),
      onUpdate: ({ editor }) => {
        const doc = this.getDoc();
        this.applied = doc;
        this.changed.emit(doc);
      },
    });

    afterNextRender(() => this.host().nativeElement.appendChild(this.element));

    effect(() => {
      const doc = this.content();
      untracked(() => {
        if (doc !== this.applied) {
          this.applied = doc;
          this.editor.chain().setMeta('addToHistory', false).setContent(doc, { emitUpdate: false }).run();
        }
      });
    });

    effect(() => {
      const editable = !this.disabled();
      untracked(() => this.editor.setEditable(editable, false));
    });

    inject(DestroyRef).onDestroy(() => this.editor.destroy());
  }

  /** The document as JSON of the contract (without empty attributes). */
  getDoc(): FaqDoc {
    return stripNulls(this.editor.getJSON() as FaqDoc);
  }

  /** Puts an image of the portal at the caret (block image). */
  insertImage(url: string, alt = 'Bild'): void {
    this.editor.chain().focus().setImage({ src: url, alt }).run();
  }

  // --- state of the toolbar (reads `version`, so that it follows the caret) ---

  protected active(name: string, attrs?: Record<string, unknown>): boolean {
    this.version();
    return this.editor.isActive(name, attrs);
  }

  protected canUndo(): boolean {
    this.version();
    return this.editor.can().undo();
  }

  protected canRedo(): boolean {
    this.version();
    return this.editor.can().redo();
  }

  protected blockValue(): string {
    this.version();
    for (const level of [1, 2, 3]) {
      if (this.editor.isActive('heading', { level })) {
        return String(level);
      }
    }
    return 'p';
  }

  protected sizeValue(): string {
    this.version();
    const size = this.editor.getAttributes('textStyle')['fontSize'];
    return safeFontSize(size) && size !== '16px' ? size : '';
  }

  protected currentColor(kind: 'color' | 'highlight'): string | null {
    this.version();
    return safeColor(this.editor.getAttributes(kind === 'color' ? 'textStyle' : 'highlight')['color']);
  }

  // --- actions ---

  private run(command: (chain: Chain) => Chain): void {
    command(this.editor.chain().focus()).run();
  }

  protected toggle(mark: 'bold' | 'italic' | 'underline' | 'strike'): void {
    const commands = { bold: (c: Chain) => c.toggleBold(), italic: (c: Chain) => c.toggleItalic(), underline: (c: Chain) => c.toggleUnderline(), strike: (c: Chain) => c.toggleStrike() };
    this.run(commands[mark]);
  }

  protected setBlock(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.run((c) => (value === 'p' ? c.setParagraph() : c.setHeading({ level: Number(value) as 1 | 2 | 3 })));
  }

  protected setFontSize(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.run((c) => (safeFontSize(value) ? c.setFontSize(value) : c.unsetFontSize().removeEmptyTextStyle()));
  }

  protected setAlign(align: Align): void {
    this.run((c) => c.setTextAlign(align));
  }

  protected list(kind: 'bullet' | 'ordered'): void {
    this.run((c) => (kind === 'bullet' ? c.toggleBulletList() : c.toggleOrderedList()));
  }

  protected quote(): void {
    this.run((c) => c.toggleBlockquote());
  }

  protected rule(): void {
    this.run((c) => c.setHorizontalRule());
  }

  protected undo(): void {
    this.run((c) => c.undo());
  }

  protected redo(): void {
    this.run((c) => c.redo());
  }

  protected clearFormat(): void {
    this.run((c) => c.unsetAllMarks().clearNodes().unsetTextAlign());
  }

  protected togglePalette(which: Exclude<Palette, null>): void {
    this.linkError.set(null);
    this.palette.update((p) => (p === which ? null : which));
  }

  protected applyColor(kind: 'color' | 'highlight', preset: Preset | null): void {
    const color = preset ? safeColor(preset.value) : null;
    if (kind === 'color') {
      this.run((c) => (color ? c.setColor(color) : c.unsetColor().removeEmptyTextStyle()));
    } else {
      this.run((c) => (color ? c.setHighlight({ color }) : c.unsetHighlight()));
    }
    this.palette.set(null);
  }

  /** The address as the portal wants it: with `http`, `https` or `mailto`; an address without scheme gets `https://`. Null if refused. */
  protected normalizeHref(raw: string): string | null {
    const text = raw.trim();
    if (!text) {
      return null;
    }
    if (/^[a-z][a-z0-9+.-]*:/i.test(text)) {
      return safeHref(text);
    }
    return safeHref(`https://${text}`);
  }

  protected applyLink(address: string): void {
    const href = this.normalizeHref(address);
    if (!href) {
      this.linkError.set('Bitte gib eine Adresse mit http, https oder mailto an.');
      return;
    }
    if (this.editor.state.selection.empty && !this.editor.isActive('link')) {
      this.linkError.set('Markiere zuerst den Text, der zum Link werden soll.');
      return;
    }
    this.linkError.set(null);
    this.run((c) => c.extendMarkRange('link').setLink({ href }));
    this.palette.set(null);
  }

  protected removeLink(): void {
    this.linkError.set(null);
    this.run((c) => c.extendMarkRange('link').unsetLink());
    this.palette.set(null);
  }

  protected currentHref(): string {
    this.version();
    const href = this.editor.getAttributes('link')['href'];
    return typeof href === 'string' ? href : '';
  }

  protected setCount(which: 'rows' | 'cols', event: Event): void {
    const max = which === 'rows' ? 20 : 10;
    const n = Math.min(max, Math.max(1, Math.trunc(Number((event.target as HTMLInputElement).value)) || 1));
    (which === 'rows' ? this.tableRows : this.tableCols).set(n);
  }

  protected insertTable(): void {
    this.run((c) => c.insertTable({ rows: this.tableRows(), cols: this.tableCols(), withHeaderRow: true }));
    this.palette.set(null);
  }

  protected table(action: 'addRow' | 'deleteRow' | 'addCol' | 'deleteCol' | 'deleteTable'): void {
    const commands = {
      addRow: (c: Chain) => c.addRowAfter(),
      deleteRow: (c: Chain) => c.deleteRow(),
      addCol: (c: Chain) => c.addColumnAfter(),
      deleteCol: (c: Chain) => c.deleteColumn(),
      deleteTable: (c: Chain) => c.deleteTable(),
    };
    this.run(commands[action]);
  }
}
