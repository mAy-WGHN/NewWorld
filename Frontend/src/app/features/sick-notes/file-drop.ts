import { Component, ElementRef, input, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { formatSize } from '../sick-note-review/sick-note-review-models';

/**
 * A drop zone for one file: drag the file onto it, or use "Datei auswählen". The file itself is checked by the page (type, size); this
 * only hands the file over and shows the choice. The page keeps the choice and passes it back in, so it can drop it again.
 * The native input stays in the page (hidden, with the given id) and is opened by the button.
 */
@Component({
  selector: 'app-file-drop',
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './file-drop.html',
})
export class FileDrop {
  readonly inputId = input.required<string>();
  readonly accept = input('');
  readonly file = input<File | null>(null);
  readonly disabled = input(false);
  readonly chosen = output<File>();
  readonly cleared = output<void>();

  protected readonly over = signal(false);
  protected readonly formatSize = formatSize;
  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');

  protected open(): void {
    this.picker().nativeElement.click();
  }

  protected onChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    // The same file can be chosen again after it was dropped or refused.
    input.value = '';
    if (file) {
      this.chosen.emit(file);
    }
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

  protected dragLeave(): void {
    this.over.set(false);
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file && !this.disabled()) {
      this.chosen.emit(file);
    }
  }
}
