import { Component, computed, input } from '@angular/core';

export type WghnAlertKind = 'error' | 'status' | 'field-error';

const CLASSES: Record<WghnAlertKind, string> = {
  error: 'm-0 rounded-xl border border-error px-4 py-2.5 text-error',
  status: 'm-0 rounded-xl bg-primary-container px-4 py-2.5 text-on-primary-container',
  'field-error': 'm-0 text-[13px] text-error',
};

/**
 * A message to the person: `error` (a failed call, red frame, read out at once), `status` (what was done, announced politely) or `field-error` (a small
 * red text under a field). The text goes between the tags; the margins are the business of the page (`class="mb-3"` on the tag), `data-testid` stays on
 * the tag as well, so pages and tests find the message where they always did.
 */
@Component({
  selector: 'wghn-alert',
  host: { class: 'block' },
  template: `<p [class]="classes()" [attr.role]="kind() === 'status' ? 'status' : 'alert'"><ng-content /></p>`,
})
export class WghnAlert {
  readonly kind = input<WghnAlertKind>('error');
  protected readonly classes = computed(() => CLASSES[this.kind()]);
}
