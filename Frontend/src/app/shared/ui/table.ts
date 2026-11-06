import { Component, Directive, Injectable, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorIntl, MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSort, MatSortModule, Sort } from '@angular/material/sort';

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

  setSort(key: string, dir: SortDir): void {
    this.sortKey.set(key);
    this.sortDir.set(dir);
    this.pageIndex.set(0);
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

/**
 * Makes a table sortable with Angular Material: `<table [appSortTable]="state">` and `<th mat-sort-header="key">Name</th>` per column.
 * Material draws the arrows and sets `aria-sort`; the state decides what is sorted. Two states only (up, down), no "unsorted".
 */
@Directive({
  selector: 'table[appSortTable]',
  hostDirectives: [MatSort],
})
export class SortTable {
  readonly table = input.required<AnyTable>({ alias: 'appSortTable' });
  private readonly sort = inject(MatSort);

  constructor() {
    this.sort.disableClear = true;
    effect(() => {
      this.sort.active = this.table().sortKey() ?? '';
      this.sort.direction = this.table().sortDir();
    });
    this.sort.sortChange.pipe(takeUntilDestroyed()).subscribe((e: Sort) => {
      if (e.active && e.direction) {
        this.table().setSort(e.active, e.direction);
      }
    });
  }
}

/** German texts of the paginator. */
@Injectable()
export class GermanPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'Zeilen pro Seite';
  override nextPageLabel = 'Nächste Seite';
  override previousPageLabel = 'Vorherige Seite';
  override firstPageLabel = 'Erste Seite';
  override lastPageLabel = 'Letzte Seite';
  override getRangeLabel = (page: number, size: number, length: number): string => {
    if (length === 0) {
      return '0 von 0';
    }
    const start = page * size;
    return `${start + 1}–${Math.min(start + size, length)} von ${length}`;
  };
}

/** Material paginator for a table state (page, page size, "1–25 von 130"). */
@Component({
  selector: 'app-pager',
  imports: [MatPaginatorModule],
  template: `
    @if (table().total() > 0) {
      <mat-paginator
        [length]="table().total()"
        [pageSize]="table().pageSize()"
        [pageIndex]="table().page()"
        [pageSizeOptions]="sizes"
        showFirstLastButtons
        (page)="changed($event)"
        aria-label="Seiten"
      />
    }
  `,
})
export class Pager {
  readonly table = input.required<AnyTable>();
  protected readonly sizes = PAGE_SIZES as unknown as number[];

  protected changed(event: PageEvent): void {
    if (event.pageSize !== this.table().pageSize()) {
      this.table().setPageSize(event.pageSize);
    } else {
      this.table().goTo(event.pageIndex);
    }
  }
}

export interface FilterOption {
  value: string;
  label: string;
}

/** A row of quick filters (one choice, the first option usually "Alle") for the filter `name` of a table: Material chips. */
@Component({
  selector: 'app-quick-filter',
  imports: [MatChipsModule],
  template: `
    <div class="flex flex-wrap items-center gap-2">
      <span class="text-sm text-on-surface-variant">{{ label() }}</span>
      <mat-chip-listbox [attr.aria-label]="label()" [value]="table().filter(name())" (change)="chosen($event.value)">
        @for (o of options(); track o.value) {
          <mat-chip-option [value]="o.value" [selected]="table().filter(name()) === o.value">{{ o.label }}</mat-chip-option>
        }
      </mat-chip-listbox>
    </div>
  `,
})
export class QuickFilter {
  readonly table = input.required<AnyTable>();
  readonly name = input.required<string>();
  readonly label = input.required<string>();
  readonly options = input.required<readonly FilterOption[]>();

  // Clicking the chosen chip again would clear the choice; "all" stays chosen instead.
  protected chosen(value: string | undefined): void {
    this.table().setFilter(this.name(), value ?? '');
  }
}

/** Search box above a table (looks in what the table's `search` option returns) with a reset for everything narrowing the table. */
@Component({
  selector: 'app-table-search',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    <div class="flex flex-wrap items-center gap-2">
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="min-w-56">
        <mat-label>{{ label() }}</mat-label>
        <mat-icon matIconPrefix>search</mat-icon>
        <input matInput type="search" [value]="table().query()" (input)="table().setQuery($any($event.target).value)" />
      </mat-form-field>
      @if (table().narrowed()) {
        <button mat-stroked-button type="button" (click)="table().reset()">Zurücksetzen</button>
      }
    </div>
  `,
})
export class TableSearch {
  readonly table = input.required<AnyTable>();
  readonly label = input('Suche');
}

export const TABLE_UI = [SortTable, MatSortModule, Pager, QuickFilter, TableSearch] as const;
