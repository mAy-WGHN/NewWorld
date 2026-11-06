import { Component, computed, inject, signal } from '@angular/core';
import { todayIso } from '../../core/dates';
import { PAGE_UI } from '../../shared/ui/material';
import { FilterOption, TABLE_UI, TableState } from '../../shared/ui/table';
import { formatDate } from '../absences/absence-models';
import { MonthClose, TimeBooking, WorkingTime, expiringSoon, formatHours, kindLabel, monthLabel } from './working-time-models';
import { WorkingTimeApi } from './working-time.api';

/** How far ahead hours that run out are pointed out. */
const WARN_DAYS = 60;

/** "Arbeitszeitkonto": balance, hours that run out soon (yellow, at the top), the bookings and the month closes of the signed-in person. */
@Component({
  selector: 'app-working-time',
  imports: [...PAGE_UI, ...TABLE_UI],
  templateUrl: './working-time.page.html',
})
export class WorkingTimePage {
  private readonly api = inject(WorkingTimeApi);

  protected readonly account = signal<WorkingTime | null>(null);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly soon = computed(() => expiringSoon(this.account()?.buckets ?? [], todayIso(), WARN_DAYS));

  protected readonly kindOptions = computed<FilterOption[]>(() => [
    { value: '', label: 'Alle' },
    ...[...new Set((this.account()?.bookings ?? []).map((b) => b.kind))].map((value) => ({ value, label: kindLabel(value) })),
  ]);

  protected readonly closeYears = computed<FilterOption[]>(() => [
    { value: '', label: 'Alle' },
    ...[...new Set((this.account()?.closes ?? []).map((c) => c.year))].sort((a, b) => b - a).map((y) => ({ value: String(y), label: String(y) })),
  ]);

  protected readonly bookingTable = new TableState<TimeBooking>(() => this.account()?.bookings ?? [], {
    columns: { date: (b) => b.date, kind: (b) => kindLabel(b.kind), hours: (b) => b.hours },
    sortKey: 'date',
    sortDir: 'desc',
    filters: { kind: (b, v) => b.kind === v },
  });

  protected readonly closeTable = new TableState<MonthClose>(() => this.account()?.closes ?? [], {
    columns: { month: (c) => c.year * 100 + c.month, worked: (c) => c.workedHours, threshold: (c) => c.thresholdHours, overtime: (c) => c.overtimeHours - c.minusHours },
    pageSize: 12,
    filters: { year: (c, v) => String(c.year) === v },
  });

  protected readonly formatHours = formatHours;
  protected readonly kindLabel = kindLabel;
  protected readonly monthLabel = monthLabel;
  protected readonly formatDate = formatDate;
  protected readonly warnDays = WARN_DAYS;

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.account.set(await this.api.mine());
      this.error.set(null);
    } catch {
      this.error.set('Das Arbeitszeitkonto konnte nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }
}
