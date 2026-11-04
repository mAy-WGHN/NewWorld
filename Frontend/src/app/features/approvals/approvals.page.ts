import { Component, computed, inject, signal } from '@angular/core';
import { PAGE_UI } from '../../shared/ui/material';
import { errorMessage, formatPeriod, problemTitle } from '../absences/absence-models';
import { PendingApproval } from './approval-models';
import { ApprovalsApi } from './approvals.api';

/**
 * "Genehmigungen": the vacation requests that wait for the decision of the signed-in person, all marked yellow. Approving is one click;
 * rejecting asks once more, because it cannot be taken back here.
 */
@Component({
  selector: 'app-approvals',
  imports: [...PAGE_UI],
  templateUrl: './approvals.page.html',
})
export class ApprovalsPage {
  private readonly api = inject(ApprovalsApi);

  protected readonly requests = signal<PendingApproval[]>([]);
  protected readonly loaded = signal(false);
  protected readonly listError = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  /** The request that is just asked "really reject?" (one at a time). */
  protected readonly confirming = signal<string | null>(null);

  protected readonly count = computed(() => this.requests().length);

  protected readonly formatPeriod = formatPeriod;

  constructor() {
    void this.load();
  }

  protected async refresh(): Promise<void> {
    this.error.set(null);
    this.status.set(null);
    this.confirming.set(null);
    await this.load();
  }

  protected ask(request: PendingApproval): void {
    this.error.set(null);
    this.confirming.set(request.absenceId);
  }

  protected cancelAsk(): void {
    this.confirming.set(null);
  }

  protected async approve(request: PendingApproval): Promise<void> {
    await this.decide(request, () => this.api.approve(request.absenceId), 'genehmigt');
  }

  protected async reject(request: PendingApproval): Promise<void> {
    await this.decide(request, () => this.api.reject(request.absenceId), 'abgelehnt');
  }

  private async decide(request: PendingApproval, call: () => Promise<unknown>, verb: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await call();
      this.confirming.set(null);
      this.status.set(`Der Urlaub von ${request.employeeName} wurde ${verb}.`);
      await this.load();
    } catch (error) {
      const title = problemTitle(error);
      this.confirming.set(null);
      if (title === 'InvalidState') {
        // somebody else was quicker: show what is still open
        this.error.set('Dieser Antrag wurde schon entschieden.');
        await this.load();
      } else {
        this.error.set(errorMessage(title));
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.requests.set(await this.api.pending());
      this.listError.set(null);
    } catch {
      this.listError.set('Die Anträge konnten nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }
}
