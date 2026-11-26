import { Component, computed, input, output } from '@angular/core';
import { PAGE_UI } from '../../shared/wghn/material';
import { currentWeek, shiftWeek, weekLabel } from './logbook-models';

/** Chooses the week: one back, one forward (not into the future) and back to this week. */
@Component({
  selector: 'app-logbook-week-nav',
  imports: [...PAGE_UI],
  template: `
    <nav class="mb-4 flex flex-wrap items-center gap-2" aria-label="Woche wählen">
      <button mat-stroked-button type="button" data-action="previous-week" (click)="weekChange.emit(shift(-1))">‹ Vorherige Woche</button>
      <strong class="min-w-48 text-center" data-testid="week-label" aria-live="polite">{{ label() }}</strong>
      <button mat-stroked-button type="button" data-action="next-week" [disabled]="isCurrent()" (click)="weekChange.emit(shift(1))">Nächste Woche ›</button>
      @if (!isCurrent()) {
        <button mat-button type="button" data-action="this-week" (click)="weekChange.emit(now)">Diese Woche</button>
      }
    </nav>
  `,
})
export class LogbookWeekNav {
  readonly week = input.required<string>();
  readonly weekChange = output<string>();

  protected readonly now = currentWeek();
  protected readonly label = computed(() => weekLabel(this.week()));
  protected readonly isCurrent = computed(() => this.week() >= this.now);

  protected shift(weeks: number): string {
    return shiftWeek(this.week(), weeks);
  }
}
