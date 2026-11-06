import { signal } from '@angular/core';
import { TableState, compareValues } from './table';

interface Row {
  name: string;
  hours: number | null;
  kind: string;
}

const rows = (n: number): Row[] => Array.from({ length: n }, (_, i) => ({ name: `Zeile ${i + 1}`, hours: i, kind: i % 2 ? 'A' : 'B' }));

const make = (source: Row[], pageSize = 10) => {
  const data = signal(source);
  const table = new TableState<Row>(() => data(), {
    columns: { name: (r) => r.name, hours: (r) => r.hours },
    pageSize,
    search: (r) => r.name,
    filters: { kind: (r, v) => r.kind === v },
  });
  return { data, table };
};

describe('TableState', () => {
  it('pages the rows and reports the range', () => {
    const { table } = make(rows(25));
    expect(table.rows().length).toBe(10);
    expect([table.firstRow(), table.lastRow(), table.total(), table.pageCount()]).toEqual([1, 10, 25, 3]);
    table.goTo(2);
    expect(table.rows().length).toBe(5);
    expect([table.firstRow(), table.lastRow()]).toEqual([21, 25]);
    table.goTo(99);
    expect(table.page()).toBe(2);
  });

  it('keeps the order of the source until a column is sorted', () => {
    const { table } = make(rows(3));
    expect(table.rows().map((r) => r.hours)).toEqual([0, 1, 2]);
  });

  it('sorts ascending, then descending, numbers as numbers and text with numbers in order', () => {
    const { table } = make(rows(12));
    table.sortBy('name');
    expect(table.rows().map((r) => r.name).slice(0, 3)).toEqual(['Zeile 1', 'Zeile 2', 'Zeile 3']);
    expect(table.ariaSort('name')).toBe('ascending');
    table.sortBy('name');
    expect(table.rows()[0].name).toBe('Zeile 12');
    expect(table.ariaSort('name')).toBe('descending');
    expect(table.ariaSort('hours')).toBe('none');
    table.sortBy('hours');
    expect(table.sortDir()).toBe('asc');
  });

  it('keeps missing values last in both directions', () => {
    const { table } = make([{ name: 'a', hours: null, kind: 'A' }, { name: 'b', hours: 2, kind: 'A' }, { name: 'c', hours: 1, kind: 'A' }]);
    table.sortBy('hours');
    expect(table.rows().map((r) => r.name)).toEqual(['c', 'b', 'a']);
    table.sortBy('hours');
    expect(table.rows().map((r) => r.name)).toEqual(['b', 'c', 'a']);
  });

  it('filters and searches, goes back to the first page and resets', () => {
    const { table } = make(rows(30));
    table.goTo(2);
    table.setFilter('kind', 'A');
    expect(table.page()).toBe(0);
    expect(table.total()).toBe(15);
    table.setQuery('zeile 3');
    expect(table.rows().map((r) => r.name)).toEqual(['Zeile 30']);
    expect(table.narrowed()).toBe(true);
    table.reset();
    expect(table.total()).toBe(30);
    expect(table.narrowed()).toBe(false);
  });

  it('never stays on a page that no longer exists when the source gets shorter', () => {
    const { data, table } = make(rows(25));
    table.goTo(2);
    data.set(rows(5));
    expect(table.page()).toBe(0);
    expect(table.rows().length).toBe(5);
  });

  it('shows no range for an empty table', () => {
    const { table } = make([]);
    expect([table.firstRow(), table.lastRow(), table.pageCount()]).toEqual([0, 0, 1]);
  });

  it('changing the page size goes back to the first page', () => {
    const { table } = make(rows(60));
    table.goTo(3);
    table.setPageSize(50);
    expect(table.page()).toBe(0);
    expect(table.rows().length).toBe(50);
  });
});

describe('compareValues', () => {
  it('compares numbers, German text and missing values', () => {
    expect(compareValues(2, 10)).toBeLessThan(0);
    expect(compareValues('Ärger', 'Zebra')).toBeLessThan(0);
    expect(compareValues('Zeile 2', 'Zeile 10')).toBeLessThan(0);
    expect(compareValues(null, 'a')).toBeGreaterThan(0);
    expect(compareValues('a', undefined)).toBeLessThan(0);
  });
});
