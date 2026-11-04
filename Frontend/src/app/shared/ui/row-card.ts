import { Component, input } from '@angular/core';

/**
 * One entry of a list: a rounded card, yellow with a strong left edge if it needs the person's attention. The class `attention` stays on
 * the element so it can be told apart.
 */
@Component({
  selector: 'app-row-card',
  host: {
    role: 'listitem',
    class: 'block rounded-xl border px-4 py-3',
    '[class.attention]': 'attention()',
    '[class.bg-attention]': 'attention()',
    '[class.border-attention-border]': 'attention()',
    '[class.text-on-attention]': 'attention()',
    '[class.border-l-6]': 'attention()',
    '[class.bg-surface-container-low]': '!attention()',
    '[class.border-outline-variant]': '!attention()',
  },
  template: '<ng-content />',
})
export class RowCard {
  readonly attention = input(false);
}
