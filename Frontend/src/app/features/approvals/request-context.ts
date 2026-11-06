import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { TeamAbsencesApi } from '../team-absences/team-absences.api';
import {
  CELL_CLASS,
  CELL_TEXT,
  DayCell,
  StoreUnavailability,
  cellsFor,
  membersOf,
} from '../team-absences/team-absences-models';
import { PendingApproval } from './approval-models';
import { contextWindow, othersAbsent, othersSummary, windowDays } from './request-context-models';

/**
 * "Wer fehlt noch?": under a vacation request the absences of the store of the applicant in the requested period (with a few days around it), so
 * that the approver sees who else is away before deciding. The request itself is marked yellow in the row of the applicant. The store service
 * decides what the approver may see; without the right the panel only says it could not be loaded.
 */
@Component({
  selector: 'app-request-context',
  template: `
    @if (error(); as message) {
      <p class="m-0 text-sm text-on-surface-variant" data-testid="context-error">{{ message }}</p>
    } @else if (!loaded()) {
      <p class="m-0 text-sm text-on-surface-variant" data-testid="context-loading">Wird geladen …</p>
    } @else {
      <p class="m-0 mb-2 text-sm" data-testid="context-summary">{{ summary() }}</p>
      <div class="overflow-x-auto rounded-xl border border-outline-variant" tabindex="0" role="region" [attr.aria-label]="'Abwesenheiten in der Filiale um den Antrag von ' + request().employeeName">
        <table class="border-collapse text-[13px]" data-testid="context-grid">
          <thead>
            <tr>
              <th scope="col" class="sticky left-0 z-10 min-w-32 bg-surface px-2 py-1 text-left font-medium">Name</th>
              @for (d of days(); track d.iso) {
                <th
                  scope="col"
                  class="w-8 min-w-8 px-0.5 py-1 text-center font-normal"
                  [class.bg-surface-container-high]="d.weekend"
                  [attr.data-iso]="d.iso"
                  [attr.data-requested]="inRequest(d.iso)"
                >
                  <div class="text-[11px] opacity-70">{{ d.weekday }}</div>
                  <div [class.font-semibold]="inRequest(d.iso)">{{ d.day }}</div>
                </th>
              }
            </tr>
          </thead>
          <tbody>
            <tr data-testid="context-row" [attr.data-id]="request().employeeId">
              <th scope="row" class="sticky left-0 z-10 bg-surface px-2 py-1 text-left font-medium whitespace-nowrap">{{ request().employeeName }} (Antrag)</th>
              @for (d of days(); track d.iso) {
                <td class="h-7 border-l border-outline-variant p-0 text-center">
                  @if (inRequest(d.iso)) {
                    <span class="flex h-full w-full items-center justify-center border border-attention-border bg-attention text-[11px] font-semibold text-on-attention" title="Beantragt" data-testid="requested-cell">?</span>
                  }
                </td>
              }
            </tr>
            @for (r of grid(); track r.id) {
              <tr data-testid="context-row" [attr.data-id]="r.id">
                <th scope="row" class="sticky left-0 z-10 bg-surface px-2 py-1 text-left font-medium whitespace-nowrap">{{ r.name }}</th>
                @for (c of r.cells; track $index) {
                  <td class="h-7 border-l border-outline-variant p-0 text-center" [class.bg-surface-container-high]="!c && days()[$index].weekend">
                    @if (c) {
                      <span class="flex h-full w-full items-center justify-center text-[11px] font-semibold" [class]="cellClass(c)" [attr.title]="c.label" [attr.aria-label]="c.label" data-testid="context-cell">{{ cellText(c) }}</span>
                    }
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class RequestContext implements OnInit {
  private readonly api = inject(TeamAbsencesApi);

  readonly request = input.required<PendingApproval>();

  protected readonly rows = signal<StoreUnavailability[]>([]);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly days = computed(() => windowDays(this.request().from, this.request().to));
  protected readonly summary = computed(() => othersSummary(othersAbsent(this.rows(), this.request().employeeId, this.request().from, this.request().to)));
  /** The colleagues with their cells for the days shown (the applicant has their own row). */
  protected readonly grid = computed(() => {
    const request = this.request();
    const days = this.days();
    return membersOf(this.rows())
      .filter((m) => m.employeeId !== request.employeeId)
      .map((m) => ({
        id: m.employeeId,
        name: m.name,
        cells: cellsFor(
          this.rows().filter((r) => r.period.employeeId === m.employeeId).map((r) => r.period),
          days,
        ),
      }));
  });

  ngOnInit(): void {
    void this.load();
  }

  protected inRequest(iso: string): boolean {
    return this.request().from <= iso && iso <= this.request().to;
  }

  protected cellClass(cell: DayCell): string {
    return CELL_CLASS[cell.kind];
  }

  protected cellText(cell: DayCell): string {
    return CELL_TEXT[cell.kind] + (cell.afterExit ? '!' : '');
  }

  private async load(): Promise<void> {
    const request = this.request();
    if (!request.storeId) {
      this.error.set('Die Filiale der Person ist nicht bekannt.');
      return;
    }
    const window = contextWindow(request.from, request.to);
    try {
      this.rows.set(await this.api.store(request.storeId, window.from, window.to));
    } catch {
      this.error.set('Die Abwesenheiten der Filiale konnten nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }
}
