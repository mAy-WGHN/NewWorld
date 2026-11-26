import { Component, ElementRef, input, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { formatFileSize } from './format';

/**
 * A drop zone for files: drag a file onto it or use "Datei auswählen" (`label` is what is dropped: "Datei", "Bild" …). The file itself is checked by the
 * page (type, size); this hands it over (`chosen`, once per file, with `multiple` for several) and, if the page passes the chosen `file` back in, shows
 * it with "Andere Datei wählen" and "Entfernen" (`cleared`). The input is hidden and opened by the button; `testId` and `chooseTestId` name the zone and
 * the button for tests.
 */
@Component({
  selector: 'wghn-file-drop',
  imports: [MatButtonModule, MatIconModule],
  template: `
    <div
      class="flex flex-col items-center gap-2 rounded-xl border-2 border-dashed px-4 py-4 text-center"
      [class.border-primary]="over()"
      [class.bg-primary-container]="over()"
      [class.border-outline]="!over()"
      [attr.data-testid]="testId()"
      (dragover)="dragOver($event)"
      (dragleave)="over.set(false)"
      (drop)="drop($event)"
    >
      <input #picker [attr.id]="inputId()" type="file" class="hidden" tabindex="-1" [multiple]="multiple()" [accept]="accept()" [disabled]="disabled()" (change)="onChange($event)" />

      @if (file(); as f) {
        <p class="m-0 flex items-center gap-2" data-testid="file-name">
          <mat-icon aria-hidden="true">description</mat-icon>
          <span>{{ f.name }} ({{ formatSize(f.size) }})</span>
        </p>
        <div class="flex flex-wrap justify-center gap-2">
          <button mat-stroked-button type="button" [attr.data-testid]="chooseTestId()" [disabled]="disabled()" (click)="open()">Andere {{ label() }} wählen</button>
          <button mat-stroked-button type="button" data-testid="remove-file" [disabled]="disabled()" (click)="cleared.emit()">Entfernen</button>
        </div>
      } @else {
        <p class="m-0 text-sm text-on-surface-variant">{{ label() }} hierher ziehen oder</p>
        <button mat-stroked-button type="button" [attr.data-testid]="chooseTestId()" [disabled]="disabled()" (click)="open()">{{ label() }} auswählen</button>
      }
    </div>
  `,
})
export class WghnFileDrop {
  readonly inputId = input<string | null>(null);
  readonly accept = input('');
  readonly file = input<File | null>(null);
  readonly disabled = input(false);
  readonly multiple = input(false);
  /** What is dropped: "Datei" or "Bild". */
  readonly label = input('Datei');
  readonly testId = input('file-drop');
  readonly chooseTestId = input('choose-file');
  readonly chosen = output<File>();
  readonly cleared = output<void>();

  protected readonly over = signal(false);
  protected readonly formatSize = formatFileSize;
  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');

  protected open(): void {
    this.picker().nativeElement.click();
  }

  protected onChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = [...(input.files ?? [])];
    // The same file can be chosen again after it was dropped or refused.
    input.value = '';
    this.hand(files);
  }

  protected dragOver(event: DragEvent): void {
    // Without this the browser would open the file in the tab instead of handing it to the page.
    event.preventDefault();
    if (this.disabled()) {
      return;
    }
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
    this.over.set(true);
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    if (!this.disabled()) {
      this.hand([...(event.dataTransfer?.files ?? [])]);
    }
  }

  private hand(files: File[]): void {
    for (const file of this.multiple() ? files : files.slice(0, 1)) {
      this.chosen.emit(file);
    }
  }
}
