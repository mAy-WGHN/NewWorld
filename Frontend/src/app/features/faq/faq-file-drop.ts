import { Component, ElementRef, input, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

/**
 * A small drop zone for one image: drag it onto the zone, or use "Bild auswählen". The page checks type and size and uploads;
 * this only hands the file over. (A reduced copy of the drop zone of the sick notes.)
 */
@Component({
  selector: 'app-faq-file-drop',
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './faq-file-drop.html',
})
export class FaqFileDrop {
  readonly accept = input('');
  readonly disabled = input(false);
  readonly chosen = output<File>();

  protected readonly over = signal(false);
  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');

  protected open(): void {
    this.picker().nativeElement.click();
  }

  protected onChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (file) {
      this.chosen.emit(file);
    }
  }

  protected dragOver(event: DragEvent): void {
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
