import { NgTemplateOutlet } from '@angular/common';
import type { WghnColumn } from '../../shared/wghn/wghn-table';
import { Component, inject, signal } from '@angular/core';
import { LogbookApi } from './logbook.api';
import { Evaluation, Figures, barHeights, distributionLabel, evaluationYears, formatAverage, formatRate } from './logbook-evaluation-models';
import { logbookError, stamp } from './logbook-models';
import { LOGBOOK_UI } from './logbook-ui';

/** One row of a table of the evaluation: a store, a region or a district. */
export interface EvaluationRow {
  id: string;
  name: string;
  /** Shown under the name, for example how many stores a region has. */
  note: string | null;
  figures: Figures;
}

/**
 * "Auswertung": the figures of a calendar year per store, region and district, for the stores the person sees (a store manager the own stores, a regional
 * leader the region, a district leader the district). The service decides what is shown; this page only presents it.
 */
@Component({
  selector: 'app-logbook-evaluation',
  imports: [NgTemplateOutlet, ...LOGBOOK_UI],
  templateUrl: './logbook-evaluation.html',
})
export class LogbookEvaluationView {
  protected readonly columns: WghnColumn[] = [
    { key: 'name', header: 'Name', rowHeader: true, class: 'font-normal' },
    { key: 'rate', header: 'Pünktlich', align: 'end' },
    { key: 'submitted', header: 'Abgegeben', align: 'end' },
    { key: 'late', header: 'Verspätet', align: 'end' },
    { key: 'missing', header: 'Fehlend', align: 'end' },
    { key: 'average', header: 'Ø Bewertung', align: 'end' },
    { key: 'distribution', header: 'Verteilung 1 bis 5' },
    { key: 'escalated', header: 'Eskaliert', align: 'end' },
    { key: 'done', header: 'Erledigt', align: 'end' },
  ];

  protected readonly rowId = (row: { id: string }): string => row.id;

  private readonly api = inject(LogbookApi);

  protected readonly years = evaluationYears(new Date().getFullYear());
  protected readonly year = signal(this.years[0]);
  protected readonly data = signal<Evaluation | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly formatRate = formatRate;
  protected readonly formatAverage = formatAverage;
  protected readonly distributionLabel = distributionLabel;
  protected readonly barHeights = barHeights;
  protected readonly stamp = stamp;

  constructor() {
    void this.load();
  }

  protected async setYear(value: string): Promise<void> {
    this.year.set(Number(value));
    await this.load();
  }

  protected districts(): EvaluationRow[] {
    return (this.data()?.districts ?? []).map((g) => ({ id: g.id, name: g.name, note: `${g.stores} Filialen`, figures: g.figures }));
  }

  protected regions(): EvaluationRow[] {
    return (this.data()?.regions ?? []).map((g) => ({ id: g.id, name: g.name, note: `${g.stores} Filialen`, figures: g.figures }));
  }

  protected stores(): EvaluationRow[] {
    return (this.data()?.stores ?? []).map((s) => ({ id: s.storeId, name: `${s.storeNumber} ${s.storeName}`.trim(), note: null, figures: s.figures }));
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.data.set(await this.api.evaluation(this.year()));
    } catch (error) {
      this.data.set(null);
      this.error.set(logbookError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
