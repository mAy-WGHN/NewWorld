import { Component, effect, input, output, signal } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { PAGE_UI } from '../../shared/ui/material';
import { Absence, canMarkElectronic, validatePeriod } from '../absences/absence-models';

/** A paper AU the person hands in afterwards: the file and the period the AU covers. */
export interface CertificateUpload {
  file: File;
  from: string;
  to: string;
}

/**
 * "AU bearbeiten" of a sick note that was reported already, on "Meine Krankmeldungen" and "Meine Abwesenheiten": hand in an AU
 * afterwards (also several, e.g. follow-up certificates) or say that the AU is an eAU (found in DATEV by the personnel department).
 * Uploading is always possible and removes the eAU mark again (the service does that). The page does the calls.
 */
@Component({
  selector: 'app-certificate-editor',
  imports: [MatCheckboxModule, ...PAGE_UI],
  templateUrl: './certificate-editor.html',
})
export class CertificateEditor {
  readonly absence = input.required<Absence>();
  readonly busy = input(false);
  readonly upload = output<CertificateUpload>();
  readonly electronic = output<boolean>();
  readonly closed = output<void>();

  protected readonly file = signal<File | null>(null);
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly error = signal<string | null>(null);

  constructor() {
    // Starts with the period of the sick note (the AU usually covers it); a new sick note resets the fields.
    effect(() => {
      const a = this.absence();
      this.from.set(a.from);
      this.to.set(a.to ?? '');
      this.file.set(null);
      this.error.set(null);
    });
  }

  protected readonly canMark = (): boolean => canMarkElectronic(this.absence());

  protected set(target: { set(value: string): void }, event: Event): void {
    target.set((event.target as HTMLInputElement).value);
  }

  protected pickFile(event: Event): void {
    this.file.set((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  protected submitUpload(): void {
    const file = this.file();
    if (!file) {
      this.error.set('Bitte eine Datei auswählen.');
      return;
    }
    const problem = validatePeriod(this.from(), this.to(), true);
    if (problem) {
      this.error.set(problem);
      return;
    }
    this.error.set(null);
    this.upload.emit({ file, from: this.from(), to: this.to() });
  }
}
