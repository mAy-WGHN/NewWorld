import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import {
  Absence,
  GENERIC_ERROR,
  VacationBalance,
  canWithdraw,
  errorMessage,
  formatPeriod,
  problemTitle,
  splitVacation,
  statusLabel,
  validatePeriod,
} from '../absences/absence-models';
import { AbsencesApi } from '../absences/absences.api';
import { VacationCalendar } from './vacation-calendar';

/**
 * "Mein Urlaub": the vacation account and the own vacation requests; "Urlaub beantragen" at the top right opens the request form.
 * Requests that still wait for a decision are listed first, apart and marked yellow.
 */
@Component({
  selector: 'app-vacation',
  imports: [NgTemplateOutlet, MatButtonToggleModule, VacationCalendar, ...PAGE_UI],
  templateUrl: './vacation.page.html',
})
export class VacationPage {
  private readonly api = inject(AbsencesApi);
  private readonly auth = inject(AuthService);

  // What the person may do; the service checks again with every call.
  protected readonly canCreate = computed(() => this.auth.privileges().has('absence:create-own'));
  protected readonly canUpdate = computed(() => this.auth.privileges().has('absence:update-own'));

  protected readonly absences = signal<Absence[]>([]);

  /** List (default) or calendar of the requests. */
  protected readonly view = signal<'list' | 'calendar'>('list');
  protected readonly loaded = signal(false);
  protected readonly listError = signal<string | null>(null);

  /** Requests that wait for a decision (first, yellow) and the others. */
  protected readonly split = computed(() => splitVacation(this.absences()));
  protected readonly total = computed(() => this.split().pending.length + this.split().others.length);

  protected readonly year = signal(new Date().getFullYear());
  protected readonly balance = signal<VacationBalance | null>(null);
  protected readonly balanceError = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  protected readonly requesting = signal(false);
  protected readonly from = signal('');
  protected readonly to = signal('');

  // Plain functions for the template.
  protected readonly statusLabel = statusLabel;
  protected readonly formatPeriod = formatPeriod;
  protected readonly canWithdraw = canWithdraw;

  constructor() {
    void this.reload();
  }

  protected set(target: { set(value: string): void }, event: Event): void {
    target.set((event.target as HTMLInputElement).value);
  }

  protected toggleRequest(): void {
    this.requesting.update((open) => !open);
    this.error.set(null);
    this.status.set(null);
  }

  protected async changeYear(delta: number): Promise<void> {
    this.year.update((y) => y + delta);
    await this.loadBalance();
  }

  protected async request(event: Event): Promise<void> {
    event.preventDefault();
    const problem = validatePeriod(this.from(), this.to(), true);
    if (problem) {
      this.error.set(problem);
      return;
    }
    await this.act('Urlaub beantragt.', () => this.api.requestVacation(this.from(), this.to()), () => {
      this.requesting.set(false);
      this.from.set('');
      this.to.set('');
    });
  }

  protected async withdraw(absence: Absence): Promise<void> {
    await this.act('Urlaub zurückgezogen.', () => this.api.withdraw(absence.id), () => undefined);
  }

  /** Runs a change, then reloads list and account; shows what the service answered if it refuses. */
  private async act(success: string, action: () => Promise<unknown>, afterSuccess: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      afterSuccess();
      this.status.set(success);
      await this.reload();
    } catch (error) {
      this.error.set(errorMessage(problemTitle(error)));
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    await Promise.all([this.loadList(), this.loadBalance()]);
  }

  private async loadList(): Promise<void> {
    try {
      this.absences.set(await this.api.list());
      this.listError.set(null);
    } catch {
      this.listError.set('Der Urlaub konnte nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }

  private async loadBalance(): Promise<void> {
    try {
      this.balance.set(await this.api.balance(this.year()));
      this.balanceError.set(null);
    } catch (error) {
      this.balance.set(null);
      this.balanceError.set(problemTitle(error) === 'NoEntitlement' ? errorMessage('NoEntitlement') : GENERIC_ERROR);
    }
  }
}
