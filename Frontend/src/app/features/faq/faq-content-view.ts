import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { FaqDoc, safeAlign, safeColor, safeFontSize, safeHref, safeImageSrc } from './faq-models';

/** A mark that is safe to show: only validated values get here. */
export interface ViewMark {
  kind: 'bold' | 'italic' | 'underline' | 'strike' | 'link' | 'highlight' | 'style';
  href?: string;
  color?: string;
  fontSize?: string;
}

/** A node of the document with only validated values; what is unknown or invalid is not in it. */
export interface ViewNode {
  kind: 'paragraph' | 'heading' | 'bulletList' | 'orderedList' | 'listItem' | 'blockquote' | 'hr' | 'table' | 'row' | 'cell' | 'image' | 'text' | 'break';
  level?: number;
  align?: string;
  start?: number;
  header?: boolean;
  colspan?: number;
  rowspan?: number;
  src?: string;
  alt?: string;
  text?: string;
  marks?: ViewMark[];
  children?: ViewNode[];
}

const MAX_DEPTH = 20;
const INLINE = new Set(['text', 'hardBreak']);
const BLOCKS = new Set(['paragraph', 'heading', 'bulletList', 'orderedList', 'blockquote', 'horizontalRule', 'table', 'image']);
/** Which children each node may have; everything else is left out. */
const CHILDREN: Record<string, Set<string>> = {
  doc: BLOCKS,
  paragraph: INLINE,
  heading: INLINE,
  bulletList: new Set(['listItem']),
  orderedList: new Set(['listItem']),
  listItem: BLOCKS,
  blockquote: BLOCKS,
  table: new Set(['tableRow']),
  tableRow: new Set(['tableHeader', 'tableCell']),
  tableHeader: BLOCKS,
  tableCell: BLOCKS,
};

function span(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isInteger(value) && value > 1 && value <= 100 ? value : undefined;
}

function marksOf(node: FaqDoc): ViewMark[] {
  const result: ViewMark[] = [];
  for (const mark of Array.isArray(node.marks) ? node.marks : []) {
    switch (mark?.type) {
      case 'bold':
      case 'italic':
      case 'underline':
      case 'strike':
        result.push({ kind: mark.type });
        break;
      case 'link': {
        const href = safeHref(mark.attrs?.['href']);
        if (href) {
          result.push({ kind: 'link', href });
        }
        break;
      }
      case 'textStyle': {
        const color = safeColor(mark.attrs?.['color']);
        const fontSize = safeFontSize(mark.attrs?.['fontSize']);
        if (color || fontSize) {
          result.push({ kind: 'style', color: color ?? undefined, fontSize: fontSize ?? undefined });
        }
        break;
      }
      case 'highlight': {
        const color = safeColor(mark.attrs?.['color']);
        result.push({ kind: 'highlight', color: color ?? undefined });
        break;
      }
    }
  }
  return result;
}

function convert(node: FaqDoc, depth: number): ViewNode | null {
  if (!node || typeof node !== 'object' || depth > MAX_DEPTH) {
    return null;
  }
  const attrs = node.attrs ?? {};
  const children = (): ViewNode[] => {
    const allowed = CHILDREN[node.type] ?? new Set<string>();
    return (Array.isArray(node.content) ? node.content : []).filter((c) => allowed.has(c?.type)).map((c) => convert(c, depth + 1)).filter((c): c is ViewNode => c !== null);
  };
  const align = safeAlign(attrs['textAlign']) ?? undefined;
  switch (node.type) {
    case 'text':
      return typeof node.text === 'string' && node.text !== '' ? { kind: 'text', text: node.text, marks: marksOf(node) } : null;
    case 'hardBreak':
      return { kind: 'break' };
    case 'paragraph':
      return { kind: 'paragraph', align, children: children() };
    case 'heading': {
      const level = attrs['level'];
      return { kind: 'heading', level: level === 1 || level === 2 || level === 3 ? level : 2, align, children: children() };
    }
    case 'bulletList':
      return { kind: 'bulletList', children: children() };
    case 'orderedList': {
      const start = attrs['start'];
      return { kind: 'orderedList', start: typeof start === 'number' && Number.isInteger(start) && start >= 0 && start < 100000 ? start : 1, children: children() };
    }
    case 'listItem':
      return { kind: 'listItem', children: children() };
    case 'blockquote':
      return { kind: 'blockquote', children: children() };
    case 'horizontalRule':
      return { kind: 'hr' };
    case 'table':
      return { kind: 'table', children: children() };
    case 'tableRow':
      return { kind: 'row', children: children() };
    case 'tableHeader':
    case 'tableCell':
      return { kind: 'cell', header: node.type === 'tableHeader', colspan: span(attrs['colspan']), rowspan: span(attrs['rowspan']), children: children() };
    case 'image': {
      const src = safeImageSrc(attrs['src']);
      return src ? { kind: 'image', src, alt: typeof attrs['alt'] === 'string' ? (attrs['alt'] as string) : '' } : null;
    }
    default:
      return null;
  }
}

/** The document as nodes with validated values only (exported for tests). */
export function toViewNodes(doc: FaqDoc | null | undefined): ViewNode[] {
  if (!doc || doc.type !== 'doc') {
    return [];
  }
  const allowed = CHILDREN['doc'];
  return (Array.isArray(doc.content) ? doc.content : []).filter((c) => allowed.has(c?.type)).map((c) => convert(c, 1)).filter((c): c is ViewNode => c !== null);
}

/**
 * Shows the rich-text answer of an FAQ entry with normal bindings only (no innerHTML): the document is turned into validated nodes first
 * (unknown nodes, unsafe links, foreign images and values outside the allowed colours/sizes are dropped) and then rendered by a recursive template.
 */
@Component({
  selector: 'app-faq-content-view',
  imports: [NgTemplateOutlet],
  templateUrl: './faq-content-view.html',
})
export class FaqContentView {
  readonly content = input<FaqDoc | null | undefined>(null);
  protected readonly nodes = computed(() => toViewNodes(this.content()));
}
