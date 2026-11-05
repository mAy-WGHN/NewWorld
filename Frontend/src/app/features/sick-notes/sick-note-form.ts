import { Component, ElementRef, input, output, signal, viewChild } from '@angular/core';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatRadioModule } from '@angular/material/radio';
import { todayIso } from '../../core/dates';
import { PAGE_UI } from '../../shared/ui/material';
import { SickNoteInput, SickNoteKind, validateSickNote } from '../absences/absence-models';

/**
 * The form "Krankmeldung", the same on "Meine Krankmeldungen" and "Meine Abwesenheiten": first or follow-up certificate (a choice,
 * nothing is preselected), first and last day (the last day is mandatory), and the AU with it: a file to upload or the note that it is an
 * eAU (found in DATEV by the personnel department). Both can also be done later ("AU bearbeiten"), so neither is mandatory.
 * The page does the calls; this form only checks what it can and hands the input over.
 */
@Component({
  selector: 'app-sick-note-form',
  imports: [MatCheckboxModule, MatRadioModule, ...PAGE_UI],
  templateUrl: './sick-note-form.html',
})
export class SickNoteForm {
  readonly busy = input(false);
  readonly submitted = output<SickNoteInput>();

  protected readonly kind = signal<SickNoteKind | null>(null);
  protected readonly from = signal(todayIso());
  protected readonly to = signal('');
  protected readonly electronic = signal(false);
  protected readonly file = signal<File | null>(null);
  protected readonly error = signal<string | null>(null);
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected set(target: { set(value: string): void }, event: Event): void {
    target.set((event.target as HTMLInputElement).value);
  }

  protected setKind(kind: SickNoteKind): void {
    this.kind.set(kind);
    this.error.set(null);
  }

  /** A file and "eAU" exclude each other: the one that is chosen last wins. */
  protected pickFile(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.file.set(file);
    if (file) {
      this.electronic.set(false);
    }
  }

  protected toggleElectronic(checked: boolean): void {
    this.electronic.set(checked);
    if (checked) {
      this.clearFile();
    }
  }

  private clearFile(): void {
    this.file.set(null);
    const input = this.fileInput();
    if (input) {
      input.nativeElement.value = '';
    }
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const input: SickNoteInput = { kind: this.kind(), from: this.from(), to: this.to(), electronic: this.electronic(), file: this.file() };
    const problem = validateSickNote(input);
    this.error.set(problem);
    if (!problem) {
      this.submitted.emit(input);
    }
  }

  /** Called by the page after the sick note was reported: the form is empty again. */
  reset(): void {
    this.kind.set(null);
    this.from.set(todayIso());
    this.to.set('');
    this.electronic.set(false);
    this.clearFile();
    this.error.set(null);
  }
}
