import { readFileSync, writeFileSync } from 'node:fs';

function edit(p, pairs) {
  let t = readFileSync(p, 'utf8').replace(/\r\n/g, '\n');
  for (const [a, b] of pairs) {
    if (!t.includes(a)) throw new Error(p + ' not found: ' + a.slice(0, 80));
    t = t.replace(a, () => b);
  }
  writeFileSync(p, t.replace(/\n/g, '\r\n'));
}

// ---------------------------------------------------------------- form
edit('sick-note-form.ts', [
  ["import { Component, ElementRef, input, output, signal, viewChild } from '@angular/core';", "import { Component, input, output, signal } from '@angular/core';"],
  ["import { CERTIFICATE_ACCEPT,", "import { FileDrop } from './file-drop';\nimport { CERTIFICATE_ACCEPT,"],
  ["  imports: [MatCheckboxModule, MatRadioModule, ...PAGE_UI],", "  imports: [FileDrop, MatCheckboxModule, MatRadioModule, ...PAGE_UI],"],
  ["  protected readonly accept = CERTIFICATE_ACCEPT;\n  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');\n", "  protected readonly accept = CERTIFICATE_ACCEPT;\n"],
  [
    `  /** A file and "eAU" exclude each other: the one that is chosen last wins. */
  protected pickFile(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    const problem = file ? validateCertificateFile(file) : null;
    if (file && problem) {
      // Not an AU: the choice is dropped at once, so a wrong file never travels.
      this.clearFile();
      this.error.set(problem);
      return;
    }
    this.error.set(null);
    this.file.set(file);
    if (file) {
      this.electronic.set(false);
    }
  }`,
    `  /** A file and "eAU" exclude each other: the one that is chosen last wins. */
  protected chooseFile(file: File): void {
    const problem = validateCertificateFile(file);
    if (problem) {
      // Not an AU: the choice is dropped at once, so a wrong file never travels.
      this.file.set(null);
      this.error.set(problem);
      return;
    }
    this.error.set(null);
    this.file.set(file);
    this.electronic.set(false);
  }`,
  ],
  [
    `  private clearFile(): void {
    this.file.set(null);
    const input = this.fileInput();
    if (input) {
      input.nativeElement.value = '';
    }
  }`,
    `  protected clearFile(): void {
    this.file.set(null);
  }`,
  ],
]);
edit('sick-note-form.html', [
  [
    `      <label class="text-sm" for="sick-file">AU (nur PDF, JPEG oder PNG)</label>
      <input
        #fileInput
        id="sick-file"
        type="file"
        accept="application/pdf,image/jpeg,image/png"
        [disabled]="busy()"
        (change)="pickFile($event)"
      />`,
    `      <span class="text-sm">AU (nur PDF, JPEG oder PNG)</span>
      <app-file-drop inputId="sick-file" [accept]="accept" [file]="file()" [disabled]="busy()" (chosen)="chooseFile($event)" (cleared)="clearFile()" />`,
  ],
]);

// ---------------------------------------------------------------- editor
edit('certificate-editor.ts', [
  ["import { PAGE_UI } from '../../shared/ui/material';", "import { PAGE_UI } from '../../shared/ui/material';\nimport { FileDrop } from './file-drop';"],
  ["  imports: [MatCheckboxModule, ...PAGE_UI],", "  imports: [FileDrop, MatCheckboxModule, ...PAGE_UI],"],
  [
    `  protected pickFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    const problem = file ? validateCertificateFile(file) : null;
    if (file && problem) {
      // Not an AU: the choice is dropped at once.
      input.value = '';
      this.file.set(null);
      this.error.set(problem);
      return;
    }
    this.error.set(null);
    this.file.set(file);
  }`,
    `  protected chooseFile(file: File): void {
    const problem = validateCertificateFile(file);
    if (problem) {
      // Not an AU: the choice is dropped at once.
      this.file.set(null);
      this.error.set(problem);
      return;
    }
    this.error.set(null);
    this.file.set(file);
  }

  protected clearFile(): void {
    this.file.set(null);
  }`,
  ],
]);
edit('certificate-editor.html', [
  [
    `    <label class="text-sm" for="cert-file">AU (nur PDF, JPEG oder PNG)</label>
    <input id="cert-file" type="file" class="mb-2" [accept]="accept" (change)="pickFile($event)" />`,
    `    <span class="text-sm">AU (nur PDF, JPEG oder PNG)</span>
    <div class="mb-2"><app-file-drop inputId="cert-file" [accept]="accept" [file]="file()" [disabled]="busy()" (chosen)="chooseFile($event)" (cleared)="clearFile()" /></div>`,
  ],
]);
console.log('applied');
