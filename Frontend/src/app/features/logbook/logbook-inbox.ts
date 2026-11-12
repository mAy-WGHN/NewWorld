import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LogbookWeekNav } from './logbook-week-nav';
import { LogbookApi } from './logbook.api';
import { ReportState, ReviewItem, STATE_LABELS, currentWeek, logbookError, stamp, stateLabel, stateOrder, weekLabel } from './logbook-models';
import { LOGBOOK_UI } from './logbook-ui';

/**
 * "Eingang" of the regional and district leaders: the handed-in reports of the stores in their reach, for one week or all open ones of all weeks.
 * What needs a look comes first: reports the store manager followed up, then new ones, then those in progress, done ones last.
 */
@Component({
  selector: 'app-logbook-inbox',
  imports: [RouterLink, LogbookWeekNav, ...LOGBOOK_UI],
  templateUrl: './logbook-inbox.html',
})
export class LogbookInbox {
  private readonly api = inject(LogbookApi);

  protected readonly week = signal(currentWeek());
  protected readonly openOnly = signal(false);
  protected readonly stateFilter = signal<ReportState | ''>('');
  protected readonly query = signal('');
  protected readonly items = signal<ReviewItem[]>([]);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly states = Object.entries(STATE_LABELS).map(([value, label]) => ({ value: value as ReportState, label }));

  /** The list the person sees: the text filter on store number and name, sorted by what needs a look. */
  protected readonly visible = computed(() => {
    const text = this.query().trim().toLowerCase();
    return this.items()
      .filter((i) => !text || `${i.storeNumber} ${i.storeName}`.toLowerCase().includes(text))
      .sort((a, b) => stateOrder(a.state) - stateOrder(b.state) || a.storeNumber.localeCompare(b.storeNumber) || b.week.localeCompare(a.week));
  });

  protected readonly stateLabel = stateLabel;
  protected readonly weekLabel = weekLabel;
  protected readonly stamp = stamp;

  constructor() {
    void this.load();
  }

  protected async setWeek(week: string): Promise<void> {
    this.week.set(week);
    await this.load();
  }

  protected async setOpenOnly(on: boolean): Promise<void> {
    this.openOnly.set(on);
    await this.load();
  }

  protected async setState(value: string): Promise<void> {
    this.stateFilter.set(value as ReportState | '');
    await this.load();
  }

  protected setQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  private async load(): Promise<void> {
    this.error.set(null);
    try {
      this.items.set(
        await this.api.list({
          week: this.openOnly() ? undefined : this.week(),
          open: this.openOnly() || undefined,
          state: this.stateFilter() || undefined,
        }),
      );
      this.loaded.set(true);
    } catch (error) {
      this.error.set(logbookError(error));
    }
  }
}
