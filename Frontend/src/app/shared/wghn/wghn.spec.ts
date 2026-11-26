import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TableState } from './table-state';
import { WghnAlert } from './wghn-alert';
import { WghnConfirmBar } from './wghn-confirm-bar';
import { WghnLoading } from './wghn-loading';
import { WghnStatCard } from './wghn-stat-card';
import { WGHN_TABLE, WghnColumn } from './wghn-table';

/** The library blocks are tested the way pages use them: inside a small host with the tags written as a page writes them. */

@Component({
  selector: 'wghn-alert-host',
  imports: [WghnAlert],
  template: `
    <wghn-alert class="mb-3" data-testid="error">Es ging schief.</wghn-alert>
    <wghn-alert kind="status">Gespeichert.</wghn-alert>
    <wghn-alert kind="field-error">Zu lang.</wghn-alert>
  `,
})
class AlertHost {}

@Component({
  selector: 'wghn-loading-host',
  imports: [WghnLoading],
  template: `<wghn-loading /><wghn-loading>Einen Moment …</wghn-loading>`,
})
class LoadingHost {}

@Component({
  selector: 'wghn-stat-host',
  imports: [WghnStatCard],
  template: `<wghn-stat-card label="Kassen gesamt" [value]="38" tone="ok" valueTestId="total" />`,
})
class StatHost {}

@Component({
  selector: 'wghn-confirm-host',
  imports: [WghnConfirmBar],
  template: `
    <wghn-confirm-bar data-testid="delete-panel" (confirmed)="answers.push('yes')" (cancelled)="answers.push('no')">Die Rolle wird gelöscht. Wirklich löschen?</wghn-confirm-bar>
    <wghn-confirm-bar variant="box" confirmAction="reset" cancelAction="cancel-reset" confirmLabel="Ja, zurücksetzen" cancelLabel="Zurück" [busy]="busy()">Zurücksetzen?</wghn-confirm-bar>
  `,
})
class ConfirmHost {
  readonly answers: string[] = [];
  readonly busy = signal(true);
}

interface Row {
  id: number;
  name: string;
  hours: number;
}

const ROWS: Row[] = [
  { id: 1, name: 'Charlie', hours: 5 },
  { id: 2, name: 'Anna', hours: 12.5 },
  { id: 3, name: 'Berta', hours: 7 },
];

const COLUMNS: WghnColumn[] = [
  { key: 'name', header: 'Name', sortable: true, rowHeader: true },
  { key: 'hours', header: 'Stunden', sortable: true, align: 'end' },
  { key: 'actions', header: 'Aktionen', hideHeader: true },
];

const newState = () => new TableState<Row>(() => ROWS, { columns: { name: (r) => r.name, hours: (r) => r.hours }, sortKey: 'name', pageSize: 2, search: (r) => r.name });

@Component({
  selector: 'wghn-plain-table-host',
  imports: [...WGHN_TABLE],
  template: `
    <wghn-table [rows]="rows" [columns]="columns" caption="Stunden je Person" testId="people" rowTestId="person" [rowId]="rowId">
      <ng-template wghnCell="actions" let-r><button type="button" [attr.data-action]="'edit-' + r.id">Bearbeiten</button></ng-template>
    </wghn-table>
  `,
})
class PlainTableHost {
  readonly rows = ROWS;
  readonly columns = COLUMNS;
  readonly rowId = (r: Row): string => String(r.id);
}

@Component({
  selector: 'wghn-wildcard-table-host',
  imports: [...WGHN_TABLE],
  template: `
    <wghn-table [rows]="rows" [columns]="columns">
      <ng-template wghnCell="*" let-r let-key="key">[{{ r[key] }}]</ng-template>
    </wghn-table>
  `,
})
class WildcardTableHost {
  readonly rows = ROWS;
  readonly columns = COLUMNS.slice(0, 2);
}

@Component({
  selector: 'wghn-empty-table-host',
  imports: [...WGHN_TABLE],
  template: `<wghn-table [rows]="[]" [columns]="columns" emptyText="Noch nichts da." />`,
})
class EmptyTableHost {
  readonly columns = COLUMNS;
}

@Component({
  selector: 'wghn-state-table-host',
  imports: [...WGHN_TABLE],
  template: `<wghn-table [state]="state" [columns]="columns" [paged]="paged" />`,
})
class StateTableHost {
  readonly state = newState();
  readonly columns = COLUMNS;
  paged = true;
}

@Component({
  selector: 'wghn-compact-table-host',
  imports: [...WGHN_TABLE],
  template: `<wghn-table [rows]="rows" [columns]="columns" density="compact" />`,
})
class CompactTableHost {
  readonly rows = ROWS;
  readonly columns: WghnColumn[] = [{ key: 'name', header: 'Name' }, { key: 'hours', header: 'Balken', decorative: true }];
}

async function render<T>(host: new () => T): Promise<{ root: HTMLElement; component: T; detect: () => void }> {
  const fixture = TestBed.createComponent(host);
  await fixture.whenStable();
  return { root: fixture.nativeElement as HTMLElement, component: fixture.componentInstance, detect: () => fixture.detectChanges() };
}

describe('wghn-alert', () => {
  it('shows an error in a red frame, read out at once, with the margin and test id of the page', async () => {
    const { root } = await render(AlertHost);

    const host = root.querySelector('wghn-alert')!;
    expect(host.classList.contains('mb-3')).toBe(true);
    expect(host.getAttribute('data-testid')).toBe('error');
    const p = host.querySelector('p')!;
    expect(p.getAttribute('role')).toBe('alert');
    expect(p.className).toContain('border-error');
    expect(p.textContent).toBe('Es ging schief.');
  });

  it('announces a status politely and draws a field error as small red text', async () => {
    const { root } = await render(AlertHost);

    const [, status, field] = [...root.querySelectorAll('wghn-alert p')];
    expect(status.getAttribute('role')).toBe('status');
    expect(status.className).toContain('bg-primary-container');
    expect(field.getAttribute('role')).toBe('alert');
    expect(field.className).toContain('text-[13px]');
    expect(field.className).not.toContain('border-error');
  });
});

describe('wghn-loading', () => {
  it('says "Wird geladen …" unless the page says something else', async () => {
    const { root } = await render(LoadingHost);

    const [first, second] = [...root.querySelectorAll('wghn-loading p')];
    expect(first.textContent).toBe('Wird geladen …');
    expect(first.getAttribute('role')).toBe('status');
    expect(second.textContent).toBe('Einen Moment …');
  });
});

describe('wghn-stat-card', () => {
  it('shows the figure with its name and marks the number for tests', async () => {
    const { root } = await render(StatHost);

    expect(root.textContent).toContain('Kassen gesamt');
    expect(root.querySelector('[data-testid="total"]')!.textContent).toBe('38');
    expect(root.querySelector('span[aria-hidden="true"]')!.className).toContain('bg-primary');
  });
});

describe('wghn-confirm-bar', () => {
  it('asks and hands over the answer: the buttons carry the usual data-action names', async () => {
    const { root, component } = await render(ConfirmHost);

    expect(root.querySelector('[data-testid="delete-panel"]')!.textContent).toContain('Wirklich löschen?');
    root.querySelector<HTMLButtonElement>('[data-action="delete"]')!.click();
    root.querySelector<HTMLButtonElement>('[data-action="cancel-delete"]')!.click();
    expect(component.answers).toEqual(['yes', 'no']);
  });

  it('takes its labels and action names from the page and is blocked while busy', async () => {
    const { root, component, detect } = await render(ConfirmHost);

    const confirm = root.querySelector<HTMLButtonElement>('[data-action="reset"]')!;
    expect(confirm.textContent).toContain('Ja, zurücksetzen');
    expect(confirm.disabled).toBe(true);
    expect(root.querySelector('[data-action="cancel-reset"]')!.textContent).toContain('Zurück');
    expect(root.querySelectorAll('wghn-confirm-bar div')[1].className).toContain('rounded-lg');
    component.busy.set(false);
    detect();
    expect(confirm.disabled).toBe(false);
  });
});

describe('wghn-table', () => {
  const headers = (root: HTMLElement) => [...root.querySelectorAll('thead th')].map((th) => th.textContent!.trim());
  const names = (root: HTMLElement) => [...root.querySelectorAll('tbody tr')].map((tr) => tr.querySelector('th')!.textContent!.trim());

  it('draws the columns, the rows and the cell templates of the page, plain values where there is no template', async () => {
    const { root } = await render(PlainTableHost);

    expect(root.querySelector('caption')!.textContent).toBe('Stunden je Person');
    expect(headers(root)).toEqual(['Name', 'Stunden', 'Aktionen']);
    expect(root.querySelector('thead th:last-child span')!.className).toContain('sr-only'); // a hidden title is still read out
    expect(root.querySelector('table')!.getAttribute('data-testid')).toBe('people');
    const rows = [...root.querySelectorAll('[data-testid="person"]')];
    expect(rows.map((r) => r.getAttribute('data-id'))).toEqual(['1', '2', '3']);
    expect(rows[0].querySelector('th[scope="row"]')!.textContent!.trim()).toBe('Charlie');
    expect(rows[0].querySelector('td[data-col="hours"]')!.textContent!.trim()).toBe('5');
    expect(rows[0].querySelector('td[data-col="hours"]')!.className).toContain('text-right');
    expect(rows[1].querySelector('[data-action="edit-2"]')).not.toBeNull();
  });

  it('serves every column without a template of its own from the wildcard template', async () => {
    const { root } = await render(WildcardTableHost);

    expect(root.querySelector('tbody tr td')!.textContent!.trim()).toBe('[5]');
  });

  it('says there is nothing to show instead of an empty table, when the page gives that text', async () => {
    const { root } = await render(EmptyTableHost);

    expect(root.querySelector('[data-testid="empty"]')!.textContent).toBe('Noch nichts da.');
    expect(root.querySelector('table')).toBeNull();
  });

  it('sorts and pages with a table state: the state decides what is shown', async () => {
    const { root, component, detect } = await render(StateTableHost);

    expect(names(root)).toEqual(['Anna', 'Berta']); // sorted by name, two per page
    expect(root.querySelector('mat-paginator')).not.toBeNull();
    component.state.setSort('hours', 'desc');
    detect();
    expect(names(root)).toEqual(['Anna', 'Berta']);
    component.state.setSort('hours', 'asc');
    detect();
    expect(names(root)).toEqual(['Charlie', 'Berta']);
    component.state.goTo(1);
    detect();
    expect(names(root)).toEqual(['Anna']);
  });

  it('turns the order when a sortable header is clicked and leaves the other headers plain', async () => {
    const { root, component, detect } = await render(StateTableHost);
    component.paged = false;
    detect();

    const sortable = [...root.querySelectorAll('thead th.mat-sort-header')].map((th) => th.textContent!.trim());
    expect(sortable).toEqual(['Name', 'Stunden']);
    root.querySelector<HTMLElement>('thead th.mat-sort-header')!.click(); // "Name", sorted ascending: now descending
    detect();
    expect(component.state.sortDir()).toBe('desc');
    expect(names(root)).toEqual(['Charlie', 'Berta']); // still the first page of two rows
  });

  it('keeps decorative cells away from screen readers and uses the compact spacing for figures', async () => {
    const { root } = await render(CompactTableHost);

    expect(root.querySelectorAll('tbody td')[1].getAttribute('aria-hidden')).toBe('true');
    expect(root.querySelectorAll('tbody td')[0].getAttribute('aria-hidden')).toBeNull();
    expect(root.querySelector('table')!.className).toContain('text-sm');
    expect(root.querySelector('tbody td')!.className).toContain('py-1.5');
  });
});
