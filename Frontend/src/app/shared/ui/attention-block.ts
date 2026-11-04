import { Component, input } from '@angular/core';

/** A block for what needs the person's attention: yellow title and hint, the rows go between the tags. */
@Component({
  selector: 'app-attention-block',
  template: `
    <section class="mb-6" [attr.aria-label]="title()">
      <h2 class="m-0 mb-1 text-lg font-medium text-on-attention">{{ title() }}</h2>
      @if (hint(); as text) {
        <p class="m-0 mb-3 text-sm text-on-surface-variant">{{ text }}</p>
      }
      <div class="flex flex-col gap-2.5" role="list">
        <ng-content />
      </div>
    </section>
  `,
})
export class AttentionBlock {
  readonly title = input.required<string>();
  readonly hint = input<string | null>(null);
}
