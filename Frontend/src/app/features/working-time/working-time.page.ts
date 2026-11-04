import { Component, computed, inject, signal } from '@angular/core';
import { todayIso } from '../../core/dates';
import { PAGE_UI } from '../../shared/ui/material';
import { formatDate } from '../absences/absence-models';
import { WorkingTime, expiringSoon, formatHours, kindLabel, monthLabel } from './working-time-models';
import { WorkingTimeApi } from './working-time.api';

/** How far ahead hours that run out are pointed out. */
const WARN_DAYS = 60;

/** "Arbeitszeitkonto": balance, hours that run out soon (yellow, at the top), the bookings and the month closes of the signed-in person. */
@Component({
  selector: 'app-working-time',
  imports: [...PAGE_UI],
  templateUrl: './working-time.page.html',
})
export class WorkingTimePage {
  private readonly api = inject(WorkingTimeApi);

  protected readonly account = signal<WorkingTime | null>(null);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly soon = computed(() => expiringSoon(this.account()?.buckets ?? [], todayIso(), WARN_DAYS));

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
