import { Component, computed, input, signal } from '@angular/core';

export type SortDir = 'asc' | 'desc';
export type SortValue = string | number | null | undefined;

export interface TableOptions<T> {
  /** How to read the value of each sortable column (numbers sort as numbers, text German and with numbers in order). */
  columns: Record<string, (row: T) => SortValue>;
  pageSize?: number;
  /** Without a sort key the rows stay in the order of the source. */
  sortKey?: string | null;
  sortDir?: SortDir;
  /** Text the search box looks in (lower case is applied here). */
  search?: (row: T) => string;
  /** Quick filters by name; a filter whose value is empty is off. */
  filters?: Record<string, (row: T, value: string) => boolean>;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type AnyTable = TableState<any>;

export const PAGE_SIZES = [10, 25, 50, 100] as const;

const collator = new Intl.Collator('de', { numeric: true, sensitivity: 'base' });

/** Nothing (`null`) sorts last in both directions. */
export function compareValues(a: SortValue, b: SortValue): number {
  const aMissing = a === null || a === undefined || a === '';
  const bMissing = b === null || b === undefined || b === '';
  if (aMissing || bMissing) {
    return aMissing === bMissing ? 0 : aMissing ? 1 : -1;
  }
  return typeof a === 'number' && typeof b === 'number' ? a - b : collator.compare(String(a), String(b));
}

/**
 * Sorting, quick filters, search and paging of one table in the browser. Every change of sorting, filter or search goes back to the first
 * page; a source that gets shorter never leaves the table on a page that does not exist any more.
 */
export class TableState<T> {
  readonly sortKey = signal<string | null>(null);
  readonly sortDir = signal<SortDir>('asc');
  readonly pageSize = signal(25);
  readonly query = signal('');
  readonly filterValues = signal<Record<string, string>>({});
  private readonly pageIndex = signal(0);

  readonly filtered = computed(() => {
    const query = this.query().trim().toLowerCase();
    const values = this.filterValues();
    const search = this.options.search;
    return this.source().filter((row) => {
      if (query && search && !search(row).toLowerCase().includes(query)) {
        return false;
      }
      return Object.entries(values).every(([name, value]) => !value || (this.options.filters?.[name]?.(row, value) ?? true));
    });
  });

  readonly sorted = computed(() => {
    const key = this.sortKey();
    const read = key ? this.options.columns[key] : undefined;
    if (!read) {
      return this.filtered();
    }
    const direction = this.sortDir() === 'asc' ? 1 : -1;
    return [...this.filtered()].sort((a, b) => {
      const x = read(a);
      const y = read(b);
      const missing = compareValues(x, y);
      // Missing values stay last whatever the direction.
      return x === null || x === undefined || x === '' || y === null || y === undefined || y === '' ? missing : direction * missing;
    });
  });

  readonly total = computed(() => this.sorted().length);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  readonly page = computed(() => Math.min(this.pageIndex(), this.pageCount() - 1));
  readonly rows = computed(() => this.sorted().slice(this.page() * this.pageSize(), (this.page() + 1) * this.pageSize()));
  /** 1-based first and last row shown (0 when there is none). */
  readonly firstRow = computed(() => (this.total() === 0 ? 0 : this.page() * this.pageSize() + 1));
  readonly lastRow = computed(() => Math.min(this.total(), (this.page() + 1) * this.pageSize()));
  /** Whether a filter or the search narrows the table (to offer "reset"). */
  readonly narrowed = computed(() => this.query().trim() !== '' || Object.values(this.filterValues()).some((v) => v));
  readonly sourceSize = computed(() => this.source().length);

  constructor(
    private readonly source: () => readonly T[],
    private readonly options: TableOptions<T>,
  ) {
    this.sortKey.set(options.sortKey ?? null);
    this.sortDir.set(options.sortDir ?? 'asc');
    this.pageSize.set(options.pageSize ?? 25);
  }

  /** First click ascending, then descending, then ascending again; another column starts ascending. */
  sortBy(key: string): void {
    if (this.sortKey() === key) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortKey.set(key);
      this.sortDir.set('asc');
    }
    this.pageIndex.set(0);
  }

  ariaSort(key: string): 'ascending' | 'descending' | 'none' {
    return this.sortKey() !== key ? 'none' : this.sortDir() === 'asc' ? 'ascending' : 'descending';
  }

  filter(name: string): string {
    return this.filterValues()[name] ?? '';
  }

  setFilter(name: string, value: string): void {
    this.filterValues.update((v) => ({ ...v, [name]: value }));
    this.pageIndex.set(0);
  }

  setQuery(value: string): void {
    this.query.set(value);
    this.pageIndex.set(0);
  }

  reset(): void {
    this.query.set('');
    this.filterValues.set({});
    this.pageIndex.set(0);
  }

  setPageSize(size: number): void {
    this.pageSize.set(size);
    this.pageIndex.set(0);
  }

  goTo(page: number): void {
    this.pageIndex.set(Math.max(0, Math.min(page, this.pageCount() - 1)));
  }
}

/** The button inside a column heading: `<th [attr.aria-sort]="t.ariaSort('x')"><button appSort="x" [table]="t">Name</button></th>`. */
@Component({
  selector: 'button[appSort]',
  host: {
    type: 'button',
    class: 'inline-flex cursor-pointer items-center gap-1 border-0 bg-transparent p-0 font-[inherit] text-inherit hover:underline',
    '(click)': 'table().sortBy(appSort())',
  },
  template: `<ng-content /><span aria-hidden="true" class="inline-block w-3 text-[11px] opacity-70">{{ arrow() }}</span>`,
})
export class SortButton {
  readonly appSort = input.required<string>();
  readonly table = input.required<AnyTable>();
  protected readonly arrow = computed(() => {
    const s = this.table().ariaSort(this.appSort());
    return s === 'ascending' ? '▲' : s === 'descending' ? '▼' : '';
  });
}

/** "1–25 von 130", previous/next page and the page size. Hidden while everything fits on one small page. */
@Component({
  selector: 'app-pager',
  template: `
    @if (table().total() > 0) {
      <nav class="mt-2 flex flex-wrap items-center justify-end gap-3 text-sm" aria-label="Seiten">
        <label class="flex items-center gap-1.5">
          Zeilen
          <select class="rounded border border-outline-variant bg-surface px-1.5 py-1" (change)="size($event)">
            @for (s of sizes; track s) {
              <option [value]="s" [selected]="s === table().pageSize()">{{ s }}</option>
            }
          </select>
        </label>
        <span data-testid="range">{{ table().firstRow() }}–{{ table().lastRow() }} von {{ table().total() }}</span>
        <button type="button" class="cursor-pointer rounded border border-outline-variant px-2.5 py-1 disabled:opacity-40" [disabled]="table().page() === 0" (click)="table().goTo(table().page() - 1)" aria-label="Vorherige Seite">‹</button>
        <button type="button" class="cursor-pointer rounded border border-outline-variant px-2.5 py-1 disabled:opacity-40" [disabled]="table().page() >= table().pageCount() - 1" (click)="table().goTo(table().page() + 1)" aria-label="Nächste Seite">›</button>
      </nav>
    }
  `,
})
export class Pager {
  readonly table = input.required<AnyTable>();
  protected readonly sizes = PAGE_SIZES;

  protected size(event: Event): void {
    this.table().setPageSize(Number((event.target as HTMLSelectElement).value));
  }
}

export interface FilterOption {
  value: string;
  label: string;
}

/** A row of quick filters (one choice, the first option usually "Alle") for the filter `name` of a table. */
@Component({
  selector: 'app-quick-filter',
  template: `
    <div class="flex flex-wrap items-center gap-1.5" role="group" [attr.aria-label]="label()">
      <span class="me-1 text-sm text-on-surface-variant">{{ label() }}</span>
      @for (o of options(); track o.value) {
        <button
          type="button"
          class="cursor-pointer rounded-full border px-3 py-1 text-sm"
          [class.bg-secondary-container]="table().filter(name()) === o.value"
          [class.border-transparent]="table().filter(name()) === o.value"
          [class.border-outline-variant]="table().filter(name()) !== o.value"
          [attr.aria-pressed]="table().filter(name()) === o.value"
          (click)="table().setFilter(name(), o.value)"
        >
          {{ o.label }}
        </button>
      }
    </div>
  `,
})
export class QuickFilter {
  readonly table = input.required<AnyTable>();
  readonly name = input.required<string>();
  readonly label = input.required<string>();
  readonly options = input.required<readonly FilterOption[]>();
}

/** Search box above a table (looks in what the table's `search` option returns) with a reset for everything narrowing the table. */
@Component({
  selector: 'app-table-search',
  template: `
    <div class="flex flex-wrap items-center gap-2">
      <label class="flex items-center gap-1.5 text-sm">
        {{ label() }}
        <input
          type="search"
          class="rounded border border-outline-variant bg-surface px-2 py-1"
          [value]="table().query()"
          (input)="table().setQuery($any($event.target).value)"
        />
      </label>
      @if (table().narrowed()) {
        <button type="button" class="cursor-pointer rounded border border-outline-variant px-2.5 py-1 text-sm" (click)="table().reset()">Zurücksetzen</button>
      }
    </div>
  `,
})
export class TableSearch {
  readonly table = input.required<AnyTable>();
  readonly label = input('Suche');
}

export const TABLE_UI = [SortButton, Pager, QuickFilter, TableSearch] as const;
