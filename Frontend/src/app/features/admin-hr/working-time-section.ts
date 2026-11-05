import { Component, computed, inject, signal } from '@angular/core';
import { PAGE_UI } from '../../shared/ui/material';
import { formatDateTime } from '../notifications/notification-models';
import {
  MonthlyThreshold,
  ReferenceEntry,
  WorkingTimeForm,
  WorkingTimeRules,
  describeError,
  loadFailed,
  thresholdHours,
  workingTimeForm,
  workingTimeRequest,
} from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';

/** One line of the monthly thresholds: the relationship (name if the person may read the list, else the id) and the hours as typed. */
interface ThresholdRow {
  id: string;
  name: string | null;
  draft: string;
  saved: string;
}

/**
 * "Arbeitszeitkonto": payout threshold and expiry for everybody (decision 31), minus hours (decision 35) and the monthly threshold per
 * employment relationship, from which on the hours of a month count as overtime. Needs `workingtime:manage-rules`.
 */
@Component({
  selector: 'app-working-time-section',
  imports: [...PAGE_UI],
  templateUrl: './working-time-section.html',
})
export class WorkingTimeSection {
  private readonly api = inject(AdminHrApi);

  protected readonly formatDateTime = formatDateTime;
  protected readonly rules = signal<WorkingTimeRules | null>(null);
  protected readonly form = signal<WorkingTimeForm | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  protected readonly rows = signal<ThresholdRow[] | null>(null);
  protected readonly namesKnown = signal(false);
  protected readonly rowBusy = signal<string | null>(null);
  protected readonly rowError = signal<{ id: string; message: string } | null>(null);
  protected readonly rowStatus = signal<string | null>(null);
  protected readonly thresholdsError = signal<string | null>(null);
  protected readonly hasRows = computed(() => (this.rows()?.length ?? 0) > 0);

  constructor() {
    void this.loadRules();
    void this.loadThresholds();
  }

  private async loadRules(): Promise<void> {
    try {
      const rules = await this.api.workingTimeRules();
      this.rules.set(rules);
      this.form.set(workingTimeForm(rules));
    } catch {
      this.error.set(loadFailed('Die Regeln des Arbeitszeitkontos'));
    }
  }

  private async loadThresholds(): Promise<void> {
    let thresholds: MonthlyThreshold[];
    try {
      thresholds = await this.api.thresholds();
    } catch {
      this.thresholdsError.set(loadFailed('Die Monatsschwellen'));
      return;
    }
    // The names come from the selection list; whoever may not read it sees the ids.
    let relationships: ReferenceEntry[] = [];
    try {
      relationships = (await this.api.listEntries('EmploymentRelationship')).filter((r) => r.isActive || thresholds.some((t) => t.relationshipId === r.id));
      this.namesKnown.set(true);
    } catch {
      this.namesKnown.set(false);
    }
    const hours = new Map(thresholds.map((t) => [t.relationshipId, String(t.hours)]));
    const rows: ThresholdRow[] = relationships.map((r) => ({ id: r.id, name: `${r.name} (${r.code})`, draft: hours.get(r.id) ?? '', saved: hours.get(r.id) ?? '' }));
    for (const [id, value] of hours) {
      if (!rows.some((r) => r.id === id)) {
        rows.push({ id, name: null, draft: value, saved: value });
      }
    }
    this.rows.set(rows);
  }

  protected patch(change: Partial<WorkingTimeForm>): void {
    this.form.update((f) => (f ? { ...f, ...change } : f));
    this.status.set(null);
  }

  protected text(field: keyof WorkingTimeForm, event: Event): void {
    this.patch({ [field]: (event.target as HTMLInputElement).value });
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (!form || this.busy()) {
      return;
    }
    const request = workingTimeRequest(form);
    if (typeof request === 'string') {
      this.error.set(request);
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await this.api.saveWorkingTimeRules(request);
      await this.loadRules();
      this.status.set('Die Regeln sind gespeichert. Sie gelten auch rückwirkend für die Berechnung der Konten.');
    } catch (e) {
      this.error.set(describeError(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected draft(id: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.rows.update((rows) => rows?.map((r) => (r.id === id ? { ...r, draft: value } : r)) ?? null);
    this.rowStatus.set(null);
  }

  protected rowLabel(row: ThresholdRow): string {
    return row.name ?? `Arbeitsverhältnis ${row.id}`;
  }

  protected async saveThreshold(row: ThresholdRow): Promise<void> {
    const hours = thresholdHours(row.draft);
    if (typeof hours === 'string') {
      this.rowError.set({ id: row.id, message: hours });
      return;
    }
    this.rowBusy.set(row.id);
    this.rowError.set(null);
    this.rowStatus.set(null);
    try {
      await this.api.saveThreshold(row.id, hours);
      const value = hours === null ? '' : String(hours);
      this.rows.update((rows) => rows?.map((r) => (r.id === row.id ? { ...r, draft: value, saved: value } : r)) ?? null);
      this.rowStatus.set(hours === null ? `Die Monatsschwelle für ${this.rowLabel(row)} ist entfernt.` : `Die Monatsschwelle für ${this.rowLabel(row)} ist gespeichert.`);
    } catch (e) {
      this.rowError.set({ id: row.id, message: describeError(e) });
    } finally {
      this.rowBusy.set(null);
    }
  }
}
