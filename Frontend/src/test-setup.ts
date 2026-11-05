// jsdom has no layout, so the browser features Angular Material asks for are replaced by plain stand-ins.

if (typeof window !== 'undefined') {
  if (!window.matchMedia) {
    window.matchMedia = (query: string): MediaQueryList =>
      ({
        matches: false,
        media: query,
        onchange: null,
        addListener: () => undefined,
        removeListener: () => undefined,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        dispatchEvent: () => false,
      }) as MediaQueryList;
  }

  if (!('ResizeObserver' in window)) {
    (window as unknown as { ResizeObserver: unknown }).ResizeObserver = class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    };
  }
}

// ProseMirror (the FAQ editor) measures the document when the selection changes; jsdom has no layout, so it gets empty measurements.
if (typeof document !== 'undefined') {
  const emptyRect = (): DOMRect => ({ x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, toJSON: () => ({}) }) as DOMRect;
  const emptyRects = (): DOMRectList => Object.assign([] as unknown as DOMRectList, { item: () => null });
  const range = Range.prototype as Partial<Range>;
  range.getClientRects ??= emptyRects;
  range.getBoundingClientRect ??= emptyRect;
  const text = Text.prototype as unknown as { getClientRects?: () => DOMRectList };
  text.getClientRects ??= emptyRects;
  document.elementFromPoint ??= () => null;
  document.elementsFromPoint ??= () => [];
  Element.prototype.scrollIntoView ??= () => undefined;
}
