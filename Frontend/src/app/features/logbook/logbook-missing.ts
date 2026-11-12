import { Component, inject, signal } from '@angular/core';
import { LogbookWeekNav } from './logbook-week-nav';
import { LogbookApi } from './logbook.api';
import { Missing, MissingStore, currentWeek, logbookError, stamp } from './logbook-models';
import { Action, LOGBOOK_UI } from './logbook-ui';

/** "Fehlende Berichte": the stores of the reach that have not handed in the report of the week, with a button to remind the store managers. */
@Component({
  selector: 'app-logbook-missing',
  imports: [LogbookWeekNav, ...LOGBOOK_UI],
  templateUrl: './logbook-missing.html',
})
export class LogbookMissing {
  private readonly api = inject(LogbookApi);

  protected readonly week = signal(currentWeek());
  protected readonly data = signal<Missing | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly remind = new Action();
  protected readonly stamp = stamp;

  constructor() {
    void this.load();
  }

  protected async setWeek(week: string): Promise<void> {
    this.week.set(week);
    this.remind.reset();
    await this.load();
  }

  protected async reminder(store: MissingStore): Promise<void> {
    const week = this.data()?.week ?? this.week();
    const ok = await this.remind.run(() => this.api.remind(store.storeId, week), `Die Filialleitung von ${store.storeName} ist erinnert.`);
    if (ok) {
      await this.load();
    }
  }

  private async load(): Promise<void> {
    this.error.set(null);
    try {
      this.data.set(await this.api.missing(this.week()));
    } catch (error) {
      this.error.set(logbookError(error));
    }
  }
}
