import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import { todayIso } from '../../core/dates';
import {
  Absence,
  GENERIC_ERROR,
  VacationBalance,
  canEnd,
  canUpload,
  canWithdraw,
  certificateLabel,
  errorMessage,
  formatDate,
  formatPeriod,
  problemTitle,
  statusLabel,
  typeLabel,
  validatePeriod,
} from './absence-models';
import { AbsencesApi } from './absences.api';

type PanelKind = 'end' | 'upload';

/** "Meine Abwesenheiten": report a sick note, request vacation, see the vacation account, end or withdraw, hand in the certificate (AU). */
@Component({
  selector: 'app-absences',
  imports: [...PAGE_UI],
  templateUrl: './absences.page.html',
})
export class AbsencesPage {
  private readonly api = inject(AbsencesApi);
  private readonly auth = inject(AuthService);

  // What the person may do; the service checks again with every call.
  protected readonly canCreate = computed(() => this.auth.privileges().has('absence:create-own'));
  protected readonly canUpdate = computed(() => this.auth.privileges().has('absence:update-own'));
  protected readonly canUploadCertificate = computed(() => this.auth.privileges().has('certificate:upload-own'));

  protected readonly absences = signal<Absence[]>([]);
  protected readonly loaded = signal(false);
  protected readonly listError = signal<string | null>(null);

  protected readonly year = signal(new Date().getFullYear());
  protected readonly balance = signal<VacationBalance | null>(null);
  protected readonly balanceError = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  protected readonly sickFrom = signal(todayIso());
  protected readonly sickTo = signal('');
  protected readonly vacationFrom = signal('');
  protected readonly vacationTo = signal('');

  protected readonly panel = signal<{ id: string; kind: PanelKind } | null>(null);
  protected readonly endTo = signal('');
  protected readonly certFile = signal<File | null>(null);
  protected readonly certFrom = signal('');
  protected readonly certTo = signal('');

  // Plain functions for the template.
  protected readonly typeLabel = typeLabel;
  protected readonly statusLabel = statusLabel;
  protected readonly certificateLabel = certificateLabel;
  protected readonly formatPeriod = formatPeriod;
  protected readonly formatDate = formatDate;
  protected readonly canWithdraw = canWithdraw;
  protected readonly canEnd = canEnd;
  protected readonly canUpload = canUpload;

  constructor() {
    void this.reload();
  }

  protected set(target: { set(value: string): void }, event: Event): void {
    target.set((event.target as HTMLInputElement).value);
  }

  protected pickFile(event: Event): void {
    this.certFile.set((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  protected openPanel(absence: Absence, kind: PanelKind): void {
    const same = this.panel()?.id === absence.id && this.panel()?.kind === kind;
    this.panel.set(same ? null : { id: absence.id, kind });
    this.error.set(null);
    this.endTo.set('');
    this.certFile.set(null);
    this.certFrom.set(absence.from);
    this.certTo.set(absence.to ?? '');
  }

  protected async changeYear(delta: number): Promise<void> {
    this.year.update((y) => y + delta);
    await this.loadBalance();
  }

  protected async reportSick(event: Event): Promise<void> {
    event.preventDefault();
    const problem = validatePeriod(this.sickFrom(), this.sickTo(), false);
    if (problem) {
      this.error.set(problem);
      return;
    }
    await this.act('Krankmeldung gemeldet. Gute Besserung!', () => this.api.reportSick(this.sickFrom(), this.sickTo() || null), () => this.sickTo.set(''));
  }

  protected async requestVacation(event: Event): Promise<void> {
    event.preventDefault();
    const problem = validatePeriod(this.vacationFrom(), this.vacationTo(), true);
    if (problem) {
      this.error.set(problem);
      return;
    }
    await this.act('Urlaub beantragt.', () => this.api.requestVacation(this.vacationFrom(), this.vacationTo()), () => {
      this.vacationFrom.set('');
      this.vacationTo.set('');
    });
  }

  protected async withdraw(absence: Absence): Promise<void> {
    await this.act('Urlaub zurückgezogen.', () => this.api.withdraw(absence.id));
  }

  protected async confirmPanel(absence: Absence): Promise<void> {
    const kind = this.panel()?.kind;
    if (kind === 'end') {
      const problem = validatePeriod(absence.from, this.endTo(), true);
      if (problem) {
        this.error.set(problem);
        return;
      }
      await this.act('Krankmeldung beendet.', () => this.api.endSick(absence.id, this.endTo()), () => this.panel.set(null));
    } else if (kind === 'upload') {
      const file = this.certFile();
      if (!file) {
        this.error.set('Bitte eine Datei auswählen.');
        return;
      }
      const problem = validatePeriod(this.certFrom(), this.certTo(), true);
      if (problem) {
        this.error.set(problem);
        return;
      }
      await this.act('AU hochgeladen. Die Personalabteilung prüft sie.', () => this.api.uploadCertificate(absence.id, file, this.certFrom(), this.certTo()), () => this.panel.set(null));
    }
  }

  /** Runs a change, then reloads list and account; shows what the service answered if it refuses. */
  private async act(success: string, action: () => Promise<unknown>, afterSuccess?: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      afterSuccess?.();
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
      this.listError.set('Die Abwesenheiten konnten nicht geladen werden.');
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
