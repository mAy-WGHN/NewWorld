import { Component, computed, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Absence, formatPeriod, statusLabel } from '../absences/absence-models';
import { MONTH_NAMES, YearMonth, monthLabel, monthOf, shiftMonth, todayIso } from '../team-absences/team-absences-models';
import { CalendarDay, VacationShown, calendarWeeks } from './vacation-calendar-models';

const WEEKDAYS = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];

/** What a day looks like, by the status of the request that covers it. */
const LOOK: Record<VacationShown, string> = {
  approved: 'bg-primary-container text-on-primary-container',
  requested: 'bg-attention text-on-attention border-attention-border',
  rejected: 'border-error text-error line-through',
};

/**
 * Month calendar of the own vacation requests. Approved, still open and rejected requests have their own look; withdrawn and cancelled ones
 * are not drawn. Each marked day says in words which request it belongs to (not by colour alone).
 */
@Component({
  selector: 'app-vacation-calendar',
  imports: [MatButtonModule, MatIconModule],
  template: `
    <section class="rounded-xl border border-outline-variant p-3" data-testid="calendar" aria-labelledby="cal-title">
      <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
        <h2 id="cal-title" class="m-0 text-lg font-medium" aria-live="polite">{{ title() }}</h2>
        <div class="flex items-center gap-1">
          <button mat-icon-button type="button" data-action="prev-month" aria-label="Vorheriger Monat" (click)="shift(-1)"><mat-icon>chevron_left</mat-icon></button>
          <button mat-stroked-button type="button" data-action="today" (click)="goToday()">Heute</button>
          <button mat-icon-button type="button" data-action="next-month" aria-label="Nächster Monat" (click)="shift(1)"><mat-icon>chevron_right</mat-icon></button>
        </div>
      </div>

      <div class="grid grid-cols-7 gap-1 text-center text-sm" role="grid" [attr.aria-label]="'Urlaub im ' + title()">
        @for (w of weekdays; track w) {
          <div class="py-1 text-[13px] text-on-surface-variant" role="columnheader">{{ w }}</div>
        }
        @for (week of weeks(); track $index) {
          @for (d of week; track d.iso) {
            <div
              role="gridcell"
              class="flex min-h-11 flex-col items-center justify-center rounded-lg border border-transparent px-1 py-1.5"
              [class.opacity-40]="!d.inMonth"
              [class.bg-surface-container-high]="d.weekend && !d.vacation"
              [class.ring-2]="d.today"
              [class.ring-primary]="d.today"
              [attr.data-iso]="d.iso"
              [attr.data-shown]="d.vacation?.shown ?? null"
              [attr.aria-current]="d.today ? 'date' : null"
              [attr.title]="titleOf(d)"
              [attr.aria-label]="labelOf(d)"
              [class]="d.vacation ? look(d) : ''"
            >
              <span [class.font-semibold]="d.today">{{ d.day }}</span>
            </div>
          }
        }
      </div>

      <ul class="m-0 mt-3 flex list-none flex-wrap gap-x-4 gap-y-1 p-0 text-[13px]" aria-label="Legende">
        <li class="flex items-center gap-1.5"><span class="inline-block size-3.5 rounded bg-primary-container"></span>Genehmigt</li>
        <li class="flex items-center gap-1.5"><span class="inline-block size-3.5 rounded border border-attention-border bg-attention"></span>Wartet auf Genehmigung</li>
        <li class="flex items-center gap-1.5"><span class="inline-block size-3.5 rounded border border-error"></span>Abgelehnt</li>
      </ul>
      @if (!any()) {
        <p class="m-0 mt-2 text-sm text-on-surface-variant" data-testid="calendar-empty">In diesem Monat ist kein Urlaub eingetragen.</p>
      }
    </section>
  `,
})
export class VacationCalendar {
  readonly absences = input.required<readonly Absence[]>();

  protected readonly weekdays = WEEKDAYS;
  protected readonly month = signal<YearMonth>(monthOf(todayIso()));
  protected readonly title = computed(() => monthLabel(this.month()));
  protected readonly weeks = computed(() => calendarWeeks(this.month(), this.absences(), todayIso()));
  protected readonly any = computed(() => this.weeks().some((w) => w.some((d) => d.inMonth && d.vacation)));

  protected shift(delta: number): void {
    this.month.update((m) => shiftMonth(m, delta));
  }

  protected goToday(): void {
    this.month.set(monthOf(todayIso()));
  }

  protected look(day: CalendarDay): string {
    return day.vacation ? `border ${LOOK[day.vacation.shown]}` : '';
  }

  protected titleOf(day: CalendarDay): string | null {
    return day.vacation ? `${statusLabel(day.vacation.status)}: ${formatPeriod(day.vacation.from, day.vacation.to)}` : null;
  }

  protected labelOf(day: CalendarDay): string {
    const [y, m, d] = day.iso.split('-').map(Number);
    const base = `${d}. ${MONTH_NAMES[m - 1]} ${y}`;
    return day.vacation ? `${base}, Urlaub ${statusLabel(day.vacation.status).toLowerCase()}` : base;
  }
}
