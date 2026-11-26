import { NgTemplateOutlet } from '@angular/common';
import { Component, Directive, TemplateRef, computed, contentChildren, inject, input } from '@angular/core';
import { MatSortModule, Sort } from '@angular/material/sort';
import { TableState, Pager } from './table-state';

/** One column of a `wghn-table`. `key` names the column for its cell template and for sorting (`TableState` options). */
export interface WghnColumn {
  key: string;
  header: string;
  /** Sortable columns need a `state` (the key is the column of the state's `options.columns`). */
  sortable?: boolean;
  /** Numbers read better on the right. */
  align?: 'start' | 'end';
  /** The first cell of a row is its name: a row header for screen readers. */
  rowHeader?: boolean;
  /** A column without a visible title (actions, bars): the title is only read out. */
  hideHeader?: boolean;
  nowrap?: boolean;
  /** Only decoration (a bar next to the figures): hidden from screen readers. */
  decorative?: boolean;
  /** Extra classes for the cells of the column (weight, width …) and for its header. */
  class?: string;
  headClass?: string;
}

/**
 * The content of a cell: `<ng-template wghnCell="status" let-row let-i="index">…</ng-template>` inside the `wghn-table` tags. Without a template the
 * cell shows `row[key]` as text.
 */
@Directive({ selector: 'ng-template[wghnCell]' })
export class WghnCell {
  readonly key = input.required<string>({ alias: 'wghnCell' });
  readonly template = inject<TemplateRef<unknown>>(TemplateRef);
}

/**
 * The table of the portal: columns and rows in, cell templates for what is more than text. With a `TableState` (`state`) the table sorts by the
 * sortable headers and pages (the state decides what is shown); without, it shows the `rows` it is given. Headers, row headers, the caption for screen
 * readers, the empty text and the spacing are the same everywhere, so a page only says which columns it has and what is in them. `rowTestId` and
 * `testId` name rows and table for tests.
 */
@Component({
  selector: 'wghn-table',
  imports: [NgTemplateOutlet, MatSortModule, Pager],
  template: `
    @if (shown().length === 0 && emptyText()) {
      <p class="m-0 text-on-surface-variant" data-testid="empty">{{ emptyText() }}</p>
    } @else {
      <div class="overflow-x-auto">
        <table
          class="w-full border-collapse text-left"
          [class.text-sm]="density() === 'compact'"
          [attr.data-testid]="testId()"
          matSort
          matSortDisableClear
          [matSortActive]="state()?.sortKey() ?? ''"
          [matSortDirection]="state()?.sortDir() ?? 'asc'"
          (matSortChange)="sortChanged($event)"
        >
          @if (caption()) {
            <caption class="sr-only">{{ caption() }}</caption>
          }
          <thead>
            <tr class="border-b border-outline-variant" [class.text-on-surface-variant]="density() === 'cozy'">
              @for (c of columns(); track c.key) {
                @if (c.sortable && state()) {
                  <th scope="col" [class]="headerClass(c)" [mat-sort-header]="c.key">{{ c.header }}</th>
                } @else {
                  <th scope="col" [class]="headerClass(c)">
                    @if (c.hideHeader) {
                      <span class="sr-only">{{ c.header }}</span>
                    } @else {
                      {{ c.header }}
                    }
                  </th>
                }
              }
            </tr>
          </thead>
          <tbody>
            @for (row of shown(); track trackBy()(row, $index); let i = $index) {
              <tr class="border-b border-outline-variant" [attr.data-testid]="rowTestId()">
                @for (c of columns(); track c.key) {
                  @if (c.rowHeader) {
                    <th scope="row" [class]="cellClass(c)">
                      <ng-container [ngTemplateOutlet]="template(c.key) ?? plain" [ngTemplateOutletContext]="{ $implicit: row, index: i, key: c.key }" />
                    </th>
                  } @else {
                    <td [class]="cellClass(c)" [attr.aria-hidden]="c.decorative ? 'true' : null">
                      <ng-container [ngTemplateOutlet]="template(c.key) ?? plain" [ngTemplateOutletContext]="{ $implicit: row, index: i, key: c.key }" />
                    </td>
                  }
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (state(); as s) {
        @if (paged()) {
          <wghn-pager [table]="s" />
        }
      }
    }

    <ng-template #plain let-row let-key="key">{{ plainValue(row, key) }}</ng-template>
  `,
})
export class WghnTable<T> {
  readonly columns = input.required<readonly WghnColumn[]>();
  /** The rows to show when there is no `state`. */
  readonly rows = input<readonly T[]>([]);
  /** Sorting and paging in the browser; the rows it shows replace `rows`. */
  readonly state = input<TableState<T> | null>(null);
  /** What the table is about, read out by screen readers. */
  readonly caption = input('');
  /** Said instead of the table when there is nothing to show (without it an empty table is drawn). */
  readonly emptyText = input<string | null>(null);
  readonly rowTestId = input<string | null>(null);
  readonly testId = input<string | null>(null);
  /** `cozy`: roomy rows for lists a person works in; `compact`: small rows for figures. */
  readonly density = input<'cozy' | 'compact'>('cozy');
  readonly paged = input(true);
  readonly trackBy = input<(row: T, index: number) => unknown>((_row, index) => index);

  private readonly cells = contentChildren(WghnCell);
  protected readonly shown = computed<readonly T[]>(() => this.state()?.rows() ?? this.rows());

  protected template(key: string): TemplateRef<unknown> | null {
    return this.cells().find((c) => c.key() === key)?.template ?? null;
  }

  protected plainValue(row: T, key: string): unknown {
    return (row as Record<string, unknown>)[key] ?? '';
  }

  protected headerClass(c: WghnColumn): string {
    return [this.cell(c), 'font-semibold', c.headClass].filter(Boolean).join(' ');
  }

  protected cellClass(c: WghnColumn): string {
    return [this.cell(c), c.class].filter(Boolean).join(' ');
  }

  private cell(c: WghnColumn): string {
    const space = this.density() === 'compact' ? 'py-1.5 pe-3' : 'px-3 py-3';
    return [space, c.align === 'end' ? 'text-right' : '', c.nowrap ? 'whitespace-nowrap' : ''].filter(Boolean).join(' ');
  }

  protected sortChanged(event: Sort): void {
    if (event.active && event.direction) {
      this.state()?.setSort(event.active, event.direction);
    }
  }
}

export const WGHN_TABLE = [WghnTable, WghnCell] as const;
