import { Component, inject, signal } from '@angular/core';
import { LogbookForm } from './logbook-form';
import { LogbookWeekNav } from './logbook-week-nav';
import { LogbookApi } from './logbook.api';
import { LogbookTemplate, MyReports, ReportSummary, currentWeek, logbookError, phaseLabel, stateLabel, weekLabel } from './logbook-models';
import { LOGBOOK_UI } from './logbook-ui';

/**
 * "Mein Bericht": the weekly report of each store the person writes for, with the week to choose and the last weeks of every store (a click opens that
 * week). Templates are loaded once per version: an old report keeps the sections it was written with.
 */
@Component({
  selector: 'app-logbook-mine',
  imports: [LogbookForm, LogbookWeekNav, ...LOGBOOK_UI],
  templateUrl: './logbook-mine.html',
})
export class LogbookMine {
  private readonly api = inject(LogbookApi);

  protected readonly week = signal(currentWeek());
  protected readonly data = signal<MyReports | null>(null);
  protected readonly templates = signal<Record<number, LogbookTemplate>>({});
  protected readonly history = signal<Record<string, ReportSummary[]>>({});
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  protected readonly phaseLabel = phaseLabel;
  protected readonly stateLabel = stateLabel;
  protected readonly weekLabel = weekLabel;

  constructor() {
    void this.load();
  }

  protected async setWeek(week: string): Promise<void> {
    this.week.set(week);
    await this.load();
  }

  protected template(version: number): LogbookTemplate | undefined {
    return this.templates()[version];
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const data = await this.api.myReports(this.week());
      const versions = [...new Set(data.stores.map((s) => s.templateVersion))].filter((v) => !(v in this.templates()));
      const loaded = await Promise.all(versions.map((v) => this.api.template(v)));
      this.templates.update((t) => ({ ...t, ...Object.fromEntries(loaded.map((tpl, i) => [versions[i], tpl])) }));
      const histories = await Promise.all(data.stores.map((s) => this.api.history(s.storeId)));
      this.history.set(Object.fromEntries(data.stores.map((s, i) => [s.storeId, histories[i]])));
      this.data.set(data);
    } catch (error) {
      this.error.set(logbookError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
