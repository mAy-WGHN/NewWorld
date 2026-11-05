import { Component, inject, signal } from '@angular/core';
import { PAGE_UI } from '../../shared/ui/material';
import { formatDateTime } from '../notifications/notification-models';
import {
  MONTHS,
  ROUNDINGS,
  ROUNDING_STEPS,
  VacationForm,
  VacationRules,
  WAITING_MODES,
  describeError,
  loadFailed,
  vacationForm,
  vacationRequest,
} from './admin-hr-models';
import { AdminHrApi } from './admin-hr.api';

/**
 * "Urlaub": the rules for everybody (decisions 24 and 28): when additional days expire, the waiting period for new people and how pro rata
 * days are rounded. Applies retroactively to every calculation. Needs `absence:manage-vacation-rules`.
 */
@Component({
  selector: 'app-vacation-section',
  imports: [...PAGE_UI],
  templateUrl: './vacation-section.html',
})
export class VacationSection {
  private readonly api = inject(AdminHrApi);

  protected readonly months = MONTHS;
  protected readonly modes = WAITING_MODES;
  protected readonly roundings = ROUNDINGS;
  protected readonly steps = ROUNDING_STEPS;
  protected readonly formatDateTime = formatDateTime;

  protected readonly rules = signal<VacationRules | null>(null);
  protected readonly form = signal<VacationForm | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const rules = await this.api.vacationRules();
      this.rules.set(rules);
      this.form.set(vacationForm(rules));
    } catch {
      this.error.set(loadFailed('Die Urlaubsregeln'));
    }
  }

  protected patch(change: Partial<VacationForm>): void {
    this.form.update((f) => (f ? { ...f, ...change } : f));
    this.status.set(null);
  }

  protected text(field: keyof VacationForm, event: Event): void {
    this.patch({ [field]: (event.target as HTMLInputElement | HTMLSelectElement).value });
  }

  protected modeHint(): string {
    return this.modes.find((m) => m.value === this.form()?.waitingMode)?.hint ?? '';
  }

  protected roundingHint(): string {
    return this.roundings.find((r) => r.value === this.form()?.rounding)?.hint ?? '';
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (!form || this.busy()) {
      return;
    }
    const request = vacationRequest(form);
    if (typeof request === 'string') {
      this.error.set(request);
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      const saved = await this.api.saveVacationRules(request);
      this.rules.set(saved);
      this.form.set(vacationForm(saved));
      this.status.set('Die Urlaubsregeln sind gespeichert. Sie gelten ab sofort auch rückwirkend für alle Berechnungen.');
    } catch (e) {
      this.error.set(describeError(e));
    } finally {
      this.busy.set(false);
    }
  }
}
