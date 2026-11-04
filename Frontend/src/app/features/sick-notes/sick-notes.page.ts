import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { todayIso } from '../../core/dates';
import {
  Absence,
  canEnd,
  canUpload,
  certificateLabel,
  errorMessage,
  formatPeriod,
  problemTitle,
  splitSickNotes,
  statusLabel,
  validatePeriod,
} from '../absences/absence-models';
import { AbsencesApi } from '../absences/absences.api';

type PanelKind = 'end' | 'upload';

/**
 * "Meine Krankmeldungen": the own sick notes; "Krankmelden" at the top right reports a new one. Sick notes for which HR still needs
 * an AU (none handed in, or turned down) are listed first, apart and marked yellow.
 */
@Component({
  selector: 'app-sick-notes',
  imports: [NgTemplateOutlet],
  templateUrl: './sick-notes.page.html',
  styleUrl: './sick-notes.page.scss',
})
export class SickNotesPage {
  private readonly api = inject(AbsencesApi);
  private readonly auth = inject(AuthService);

  // What the person may do; the service checks again with every call.
  protected readonly canCreate = computed(() => this.auth.privileges().has('absence:create-own'));
  protected readonly canUpdate = computed(() => this.auth.privileges().has('absence:update-own'));
  protected readonly canUploadCertificate = computed(() => this.auth.privileges().has('certificate:upload-own'));

  private readonly absences = signal<Absence[]>([]);
  protected readonly loaded = signal(false);
  protected readonly listError = signal<string | null>(null);

  /** Sick notes that still need an AU (first, yellow) and the others. */
  protected readonly split = computed(() => splitSickNotes(this.absences()));
  protected readonly total = computed(() => this.split().attention.length + this.split().others.length);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);

  protected readonly reporting = signal(false);
  protected readonly sickFrom = signal(todayIso());
  protected readonly sickTo = signal('');

  protected readonly panel = signal<{ id: string; kind: PanelKind } | null>(null);
  protected readonly endTo = signal('');
  protected readonly certFile = signal<File | null>(null);
  protected readonly certFrom = signal('');
  protected readonly certTo = signal('');

  // Plain functions for the template.
  protected readonly statusLabel = statusLabel;
  protected readonly certificateLabel = certificateLabel;
  protected readonly formatPeriod = formatPeriod;
  protected readonly canEnd = canEnd;
  protected readonly canUpload = canUpload;

  constructor() {
    void this.load();
  }

  protected set(target: { set(value: string): void }, event: Event): void {
    target.set((event.target as HTMLInputElement).value);
  }

  protected pickFile(event: Event): void {
    this.certFile.set((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  protected toggleReport(): void {
    this.reporting.update((open) => !open);
    this.error.set(null);
    this.status.set(null);
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

  protected async report(event: Event): Promise<void> {
    event.preventDefault();
    const problem = validatePeriod(this.sickFrom(), this.sickTo(), false);
    if (problem) {
      this.error.set(problem);
      return;
    }
    await this.act('Krankmeldung gemeldet. Gute Besserung!', () => this.api.reportSick(this.sickFrom(), this.sickTo() || null), () => {
      this.reporting.set(false);
      this.sickTo.set('');
    });
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

  /** Runs a change, then reloads the list; shows what the service answered if it refuses. */
  private async act(success: string, action: () => Promise<unknown>, afterSuccess: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      afterSuccess();
      this.status.set(success);
      await this.load();
    } catch (error) {
      this.error.set(errorMessage(problemTitle(error)));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.absences.set(await this.api.list());
      this.listError.set(null);
    } catch {
      this.listError.set('Die Krankmeldungen konnten nicht geladen werden.');
    } finally {
      this.loaded.set(true);
    }
  }
}
