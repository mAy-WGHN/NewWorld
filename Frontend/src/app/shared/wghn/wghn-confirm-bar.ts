import { Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

/**
 * The question before something is taken away ("Die Rolle wird gelöscht. Wirklich löschen?") with the two answers. The question goes between the tags;
 * `variant="line"` is a line under the entry it belongs to, `variant="box"` a framed bar of its own. The buttons carry `data-action` names (`delete` and
 * `cancel-delete` unless the page says otherwise) so tests find them the same way everywhere.
 */
@Component({
  selector: 'wghn-confirm-bar',
  imports: [MatButtonModule],
  host: { class: 'block' },
  template: `
    <div
      class="flex flex-wrap items-center gap-2"
      [class.mt-3]="variant() === 'line'"
      [class.border-t]="variant() === 'line'"
      [class.border-outline-variant]="true"
      [class.pt-3]="variant() === 'line'"
      [class.rounded-lg]="variant() === 'box'"
      [class.border]="variant() === 'box'"
      [class.px-3]="variant() === 'box'"
      [class.py-2]="variant() === 'box'"
    >
      <span class="text-sm"><ng-content /></span>
      <button mat-flat-button type="button" [attr.data-action]="confirmAction()" [disabled]="busy()" (click)="confirmed.emit()">{{ confirmLabel() }}</button>
      <button mat-stroked-button type="button" [attr.data-action]="cancelAction()" (click)="cancelled.emit()">{{ cancelLabel() }}</button>
    </div>
  `,
})
export class WghnConfirmBar {
  readonly variant = input<'line' | 'box'>('line');
  readonly confirmLabel = input('Ja, löschen');
  readonly cancelLabel = input('Abbrechen');
  readonly busy = input(false);
  readonly confirmAction = input('delete');
  readonly cancelAction = input('cancel-delete');
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();
}
