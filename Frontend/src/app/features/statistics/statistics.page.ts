import { Component, computed, inject, signal } from '@angular/core';
import { PAGE_UI } from '../../shared/ui/material';
import { envelopeMessage } from '../reports/report-models';
import {
  LocationSeverityStatistic,
  MonthRange,
  MonthlyCategoryStatistic,
  MonthlyStatistic,
  PersonnelNumberStatistic,
  barPercent,
  maxOf,
  monthLabel,
  rangeProblem,
} from './statistics-models';
import { StatisticsApi } from './statistics.api';

/**
 * "Statistik": how many reports were filed, by month, category, store and personnel number, within the months chosen. Only the reports
 * the person's privilege reaches are counted (the portal decides). Tables carry every number; the bars only help to compare.
 */
@Component({
  selector: 'app-statistics',
  imports: [...PAGE_UI],
  templateUrl: './statistics.page.html',
})
export class StatisticsPage {
  private readonly api = inject(StatisticsApi);

  protected readonly range = signal<MonthRange>({ from: '', to: '' });
  protected readonly monthly = signal<MonthlyStatistic[]>([]);
  protected readonly byCategory = signal<MonthlyCategoryStatistic[]>([]);
  protected readonly byLocation = signal<LocationSeverityStatistic[]>([]);
  protected readonly byPerson = signal<PersonnelNumberStatistic[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly monthlyMax = computed(() => maxOf(this.monthly().map((r) => r.total)));
  protected readonly categoryMax = computed(() => maxOf(this.byCategory().map((r) => r.total)));
  protected readonly locationMax = computed(() => maxOf(this.byLocation().map((r) => r.total)));
  protected readonly personMax = computed(() => maxOf(this.byPerson().map((r) => r.total)));

  protected readonly monthLabel = monthLabel;
  protected readonly barPercent = barPercent;

  constructor() {
    void this.load();
  }

  protected setFrom(event: Event): void {
    this.range.update((r) => ({ ...r, from: (event.target as HTMLInputElement).value }));
  }

  protected setTo(event: Event): void {
    this.range.update((r) => ({ ...r, to: (event.target as HTMLInputElement).value }));
  }

  protected async reset(): Promise<void> {
    this.range.set({ from: '', to: '' });
    await this.load();
  }

  protected async load(): Promise<void> {
    const problem = rangeProblem(this.range());
    if (problem) {
      this.error.set(problem);
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const range = this.range();
      const [monthly, byCategory, byLocation, byPerson] = await Promise.all([
        this.api.monthly(range),
        this.api.monthlyByCategory(range),
        this.api.byLocationSeverity(range),
        this.api.byPersonnelNumber(range),
      ]);
      this.monthly.set(monthly);
      this.byCategory.set(byCategory);
      this.byLocation.set(byLocation);
      this.byPerson.set(byPerson);
    } catch (error) {
      this.error.set(envelopeMessage(error));
    } finally {
      this.busy.set(false);
      this.loaded.set(true);
    }
  }
}
