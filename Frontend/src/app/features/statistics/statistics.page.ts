import { Component, computed, inject, signal } from '@angular/core';
import { PAGE_UI } from '../../shared/ui/material';
import { FilterOption, TABLE_UI, TableState } from '../../shared/ui/table';
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
  imports: [...PAGE_UI, ...TABLE_UI],
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

  protected readonly criticalOptions: FilterOption[] = [
    { value: '', label: 'Alle' },
    { value: 'critical', label: 'Mit kritischen' },
  ];

  // Months are "YYYY-MM": text sorting is time sorting. The year filter offers the years that occur.
  private static years(months: string[]): FilterOption[] {
    return [{ value: '', label: 'Alle' }, ...[...new Set(months.map((m) => m.slice(0, 4)))].sort().reverse().map((y) => ({ value: y, label: y }))];
  }

  protected readonly monthlyYears = computed(() => StatisticsPage.years(this.monthly().map((r) => r.month)));
  protected readonly categoryYears = computed(() => StatisticsPage.years(this.byCategory().map((r) => r.month)));

  protected readonly monthlyTable = new TableState<MonthlyStatistic>(() => this.monthly(), {
    columns: { month: (r) => r.month, employee: (r) => r.employee, system: (r) => r.system, total: (r) => r.total },
    pageSize: 12,
    filters: { year: (r, v) => r.month.startsWith(v) },
  });

  protected readonly categoryTable = new TableState<MonthlyCategoryStatistic>(() => this.byCategory(), {
    columns: { month: (r) => r.month, bug: (r) => r.bug, virus: (r) => r.virus, theft: (r) => r.theft, other: (r) => r.other, suggestion: (r) => r.suggestion, total: (r) => r.total },
    pageSize: 12,
    filters: { year: (r, v) => r.month.startsWith(v) },
  });

  protected readonly locationTable = new TableState<LocationSeverityStatistic>(() => this.byLocation(), {
    columns: { name: (r) => r.locationName || 'Unbekannte Filiale', low: (r) => r.low, medium: (r) => r.medium, high: (r) => r.high, critical: (r) => r.critical, total: (r) => r.total },
    search: (r) => r.locationName || 'Unbekannte Filiale',
    filters: { critical: (r) => r.critical > 0 },
  });

  protected readonly personTable = new TableState<PersonnelNumberStatistic>(() => this.byPerson(), {
    columns: { personnel: (r) => r.personnelNumber, low: (r) => r.low, medium: (r) => r.medium, high: (r) => r.high, critical: (r) => r.critical, total: (r) => r.total },
    search: (r) => String(r.personnelNumber),
    filters: { critical: (r) => r.critical > 0 },
  });
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
