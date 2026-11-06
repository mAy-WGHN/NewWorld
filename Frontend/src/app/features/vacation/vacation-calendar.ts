import { Component, ElementRef, Injector, afterNextRender, computed, effect, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Absence, formatPeriod, statusLabel } from '../absences/absence-models';
import { MONTH_NAMES, YearMonth, monthLabel, monthOf, shiftMonth, todayIso } from '../team-absences/team-absences-models';
import { CalendarDay, Holiday, VacationShown, calendarWeeks, inRange, keyTarget, selectionRange } from './vacation-calendar-models';

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
  host: { '(document:pointerup)': 'finish()', '(document:pointercancel)': 'cancelDrag()', '(document:keydown.escape)': 'clear()' },
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

      @if (selectable()) {
        <p class="m-0 mb-2 text-sm text-on-surface-variant">Tag anklicken oder über mehrere Tage ziehen, um Urlaub zu beantragen. Mit der Tastatur: Pfeiltasten wechseln den Tag, Enter beginnt die Auswahl, die Pfeiltasten dehnen sie aus, Enter beendet sie, Esc bricht ab.</p>
      }
      <div
        class="grid grid-cols-7 gap-1 text-center text-sm select-none"
        [class.touch-none]="selectable()"
        role="grid"
        [attr.aria-label]="'Urlaub im ' + title()"
        (pointerdown)="down($event)"
        (pointermove)="move($event)"
        (keydown)="key($event)"
      >
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
              [class.text-tertiary]="!!d.holiday && !d.vacation"
              [class.border-tertiary]="!!d.holiday && !d.vacation"
              [class.ring-2]="d.today"
              [class.ring-primary]="d.today"
              [class.cursor-pointer]="selectable()"
              [class.!bg-secondary-container]="picked(d)"
              [class.!text-on-secondary-container]="picked(d)"
              [attr.data-picked]="picked(d)"
              [attr.data-iso]="d.iso"
              [attr.data-shown]="d.vacation?.shown ?? null"
              [attr.aria-current]="d.today ? 'date' : null"
              [attr.tabindex]="d.iso === tabbable() ? 0 : -1"
              [attr.aria-selected]="selectable() ? picked(d) : null"
              [attr.title]="titleOf(d)"
              [attr.data-holiday]="d.holiday?.name ?? null"
              [attr.aria-label]="labelOf(d)"
              [class]="d.vacation ? look(d) : ''"
            >
              <span [class.font-semibold]="d.today">{{ d.day }}</span>
              @if (d.holiday; as h) {
                <span class="hidden max-w-full truncate text-[10px] leading-tight sm:block" [class.underline]="h.storeOpens">{{ h.name }}</span>
              }
            </div>
          }
        }
      </div>

      <ul class="m-0 mt-3 flex list-none flex-wrap gap-x-4 gap-y-1 p-0 text-[13px]" aria-label="Legende">
        <li class="flex items-center gap-1.5"><span class="inline-block size-3.5 rounded bg-primary-container"></span>Genehmigt</li>
        <li class="flex items-center gap-1.5"><span class="inline-block size-3.5 rounded border border-attention-border bg-attention"></span>Wartet auf Genehmigung</li>
        <li class="flex items-center gap-1.5"><span class="inline-block size-3.5 rounded border border-error"></span>Abgelehnt</li>
        <li class="flex items-center gap-1.5"><span class="inline-block size-3.5 rounded border border-tertiary"></span>Feiertag</li>
      </ul>
      @if (selection(); as s) {
        <div class="mt-3 flex flex-wrap items-center gap-3 rounded-xl bg-secondary-container px-4 py-2.5 text-on-secondary-container" role="status" data-testid="selection">
          <span>Urlaub {{ period(s) }} beantragen?</span>
          <button mat-flat-button type="button" data-action="confirm" [disabled]="busy()" (click)="confirm()">Beantragen</button>
          <button mat-stroked-button type="button" data-action="discard" (click)="clear()">Abbrechen</button>
        </div>
      }
      @if (error(); as message) {
        <p class="m-0 mt-2 rounded-xl border border-error px-4 py-2.5 text-error" role="alert" data-testid="calendar-error">{{ message }}</p>
      }
      @if (!any()) {
        <p class="m-0 mt-2 text-sm text-on-surface-variant" data-testid="calendar-empty">In diesem Monat ist kein Urlaub eingetragen.</p>
      }
    </section>
  `,
})
export class VacationCalendar {
  readonly absences = input.required<readonly Absence[]>();
  /** Whether days can be picked to request vacation (needs the privilege); the service checks again. */
  readonly selectable = input(false);
  /** Statutory holidays of the person's store for the days shown; the page loads them for the range this calendar announces. */
  readonly holidays = input<readonly Holiday[]>([]);
  /** The days on show (first and last day of the grid): the page loads the holidays for them. */
  readonly shown = output<{ from: string; to: string }>();
  readonly busy = input(false);
  readonly error = input<string | null>(null);
  /** The person confirmed a period. */
  readonly requested = output<{ from: string; to: string }>();

  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  /** The day the keyboard focus is on (kept when the month changes as long as it is still on show). */
  private readonly focusIso = signal<string>(todayIso());
  /** A pick started with Enter that the arrow keys extend. */
  private readonly keying = signal(false);
  private readonly anchor = signal<string | null>(null);
  private readonly current = signal<string | null>(null);
  private readonly dragging = signal(false);
  /** The period picked (while dragging and until it is confirmed or dropped). */
  protected readonly range = computed(() => {
    const a = this.anchor();
    const c = this.current();
    return a && c ? selectionRange(a, c) : null;
  });
  /** Set when the pointer was released: waits for "Beantragen". */
  protected readonly selection = signal<{ from: string; to: string } | null>(null);
  protected readonly period = (r: { from: string; to: string }): string => (r.from === r.to ? `am ${formatPeriod(r.from, null)}` : `${formatPeriod(r.from, r.to)}`);

  protected readonly weekdays = WEEKDAYS;
  protected readonly month = signal<YearMonth>(monthOf(todayIso()));
  protected readonly title = computed(() => monthLabel(this.month()));
  protected readonly weeks = computed(() => calendarWeeks(this.month(), this.absences(), todayIso(), this.holidays()));

  constructor() {
    effect(() => {
      const weeks = this.weeks();
      this.shown.emit({ from: weeks[0][0].iso, to: weeks.at(-1)![6].iso });
    });
  }
  /** The one day in the tab order: the focused one if it is on show, else the first of the month. */
  protected readonly tabbable = computed(() => {
    const days = this.weeks().flat();
    const focus = this.focusIso();
    return days.some((d) => d.iso === focus) ? focus : days.find((d) => d.inMonth)!.iso;
  });
  protected readonly any = computed(() => this.weeks().some((w) => w.some((d) => d.inMonth && d.vacation)));

  protected picked(d: CalendarDay): boolean {
    return inRange(d.iso, this.selection() ?? this.range());
  }

  private isoAt(x: number, y: number): string | null {
    return (document.elementFromPoint(x, y)?.closest('[data-iso]') as HTMLElement | null)?.dataset['iso'] ?? null;
  }

  protected down(event: PointerEvent): void {
    const iso = this.selectable() && event.button === 0 ? this.isoAt(event.clientX, event.clientY) : null;
    if (!iso) {
      return;
    }
    event.preventDefault();
    (event.target as Element).releasePointerCapture?.(event.pointerId); // touch: keep getting moves over other days
    this.keying.set(false);
    this.selection.set(null);
    this.anchor.set(iso);
    this.current.set(iso);
    this.dragging.set(true);
  }

  protected move(event: PointerEvent): void {
    if (this.dragging()) {
      const iso = this.isoAt(event.clientX, event.clientY);
      if (iso) {
        this.current.set(iso);
      }
    }
  }

  /** Pointer released: the period waits for a confirmation (a slip of the finger must not send a request). */
  protected finish(): void {
    if (this.dragging()) {
      this.dragging.set(false);
      this.selection.set(this.range());
    }
  }

  protected cancelDrag(): void {
    this.dragging.set(false);
    this.clear();
  }

  /** Keyboard: arrows move the focus (and extend a pick), Enter or Space starts and ends a pick. Esc is handled on the document. */
  protected key(event: KeyboardEvent): void {
    const from = (event.target as HTMLElement).closest<HTMLElement>('[data-iso]')?.dataset['iso'];
    if (!from || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      if (!this.selectable()) {
        return;
      }
      if (this.keying()) {
        this.keying.set(false);
        this.selection.set(this.range());
      } else {
        this.selection.set(null);
        this.anchor.set(from);
        this.current.set(from);
        this.keying.set(true);
      }
      return;
    }
    const target = keyTarget(from, event.key);
    if (!target) {
      return;
    }
    event.preventDefault();
    if (!this.weeks().some((w) => w.some((d) => d.iso === target))) {
      this.month.set(monthOf(target));
    }
    this.focusIso.set(target);
    if (this.keying()) {
      this.current.set(target);
    }
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(`[data-iso="${target}"]`)?.focus(), { injector: this.injector });
  }

  protected clear(): void {
    this.keying.set(false);
    this.dragging.set(false);
    this.anchor.set(null);
    this.current.set(null);
    this.selection.set(null);
  }

  protected confirm(): void {
    const s = this.selection();
    if (s) {
      this.requested.emit(s);
      this.clear();
    }
  }

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
    const parts = [
      day.holiday ? `${day.holiday.name}${day.holiday.storeOpens ? ' (deine Filiale ist geöffnet)' : ''}` : null,
      day.vacation ? `${statusLabel(day.vacation.status)}: ${formatPeriod(day.vacation.from, day.vacation.to)}` : null,
    ].filter((p): p is string => p !== null);
    return parts.length ? parts.join(' · ') : null;
  }

  protected labelOf(day: CalendarDay): string {
    const [y, m, d] = day.iso.split('-').map(Number);
    const base = `${d}. ${MONTH_NAMES[m - 1]} ${y}`;
    const holiday = day.holiday ? `, Feiertag ${day.holiday.name}` : '';
    return day.vacation ? `${base}${holiday}, Urlaub ${statusLabel(day.vacation.status).toLowerCase()}` : `${base}${holiday}`;
  }
}
