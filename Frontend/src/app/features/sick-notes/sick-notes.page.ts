import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { PAGE_UI } from '../../shared/ui/material';
import {
  Absence,
  SickNoteInput,
  canEnd,
  canUpload,
  certificateLabel,
  errorMessage,
  formatPeriod,
  kindLabel,
  problemTitle,
  splitSickNotes,
  statusLabel,
  validatePeriod,
} from '../absences/absence-models';
import { AbsencesApi } from '../absences/absences.api';
import { CertificateEditor, CertificateUpload } from './certificate-editor';
import { SickNoteForm } from './sick-note-form';

/** `end` gives an old sick note without a last day its end, `certificate` is "AU bearbeiten". */
type PanelKind = 'end' | 'certificate';

/**
 * "Meine Krankmeldungen": the own sick notes; "Krankmelden" at the top right reports a new one. Sick notes for which HR still needs
 * an AU (none handed in, or turned down) are listed first, apart and marked yellow.
 */
@Component({
  selector: 'app-sick-notes',
  imports: [NgTemplateOutlet, CertificateEditor, SickNoteForm, ...PAGE_UI],
  templateUrl: './sick-notes.page.html',
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
  private readonly sickForm = viewChild(SickNoteForm);

  protected readonly panel = signal<{ id: string; kind: PanelKind } | null>(null);
  protected readonly endTo = signal('');

  // Plain functions for the template.
  protected readonly statusLabel = statusLabel;
  protected readonly certificateLabel = certificateLabel;
  protected readonly formatPeriod = formatPeriod;
  protected readonly kindLabel = kindLabel;
  protected readonly canEnd = canEnd;
  protected readonly canUpload = canUpload;

  constructor() {
    void this.load();
  }

  protected set(target: { set(value: string): void }, event: Event): void {
    target.set((event.target as HTMLInputElement).value);
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
  }

  /** Reports the sick note, with the AU if the person handed one in; a failed upload does not undo the sick note. */
  protected async report(input: SickNoteInput): Promise<void> {
    let certificateFailed = false;
    await this.act(
      () => (input.file && !certificateFailed ? 'Krankmeldung gemeldet, die AU ist hochgeladen. Gute Besserung!' : 'Krankmeldung gemeldet. Gute Besserung!'),
      async () => {
        certificateFailed = (await this.api.reportSickNote(input)).certificateFailed;
      },
      () => {
        this.sickForm()?.reset();
        this.reporting.set(false);
        if (certificateFailed) {
          this.error.set('Die Krankmeldung ist gemeldet, aber die AU konnte nicht hochgeladen werden. Bitte reiche sie über "AU bearbeiten" nach.');
        }
      },
    );
  }

  protected async uploadCertificate(absence: Absence, upload: CertificateUpload): Promise<void> {
    await this.act(() => 'AU hochgeladen. Die Personalabteilung prüft sie.', () => this.api.uploadCertificate(absence.id, upload.file, upload.from, upload.to), () => this.panel.set(null));
  }

  protected async setElectronic(absence: Absence, electronic: boolean): Promise<void> {
    await this.act(
      () => (electronic ? 'Als eAU gekennzeichnet. Die Personalabteilung findet sie in DATEV.' : 'Die eAU-Kennzeichnung wurde entfernt.'),
      () => this.api.setElectronic(absence.id, electronic),
      () => undefined,
    );
  }

  protected async confirmPanel(absence: Absence): Promise<void> {
    const kind = this.panel()?.kind;
    if (kind === 'end') {
      const problem = validatePeriod(absence.from, this.endTo(), true);
      if (problem) {
        this.error.set(problem);
        return;
      }
      await this.act(() => 'Krankmeldung beendet.', () => this.api.endSick(absence.id, this.endTo()), () => this.panel.set(null));
    }
  }

  /** Runs a change, then reloads the list; shows what the service answered if it refuses. */
  private async act(success: () => string, action: () => Promise<unknown>, afterSuccess: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    try {
      await action();
      this.status.set(success());
      afterSuccess();
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
