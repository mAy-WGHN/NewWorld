import { Component, input } from '@angular/core';

/** A figure with its name above ("Kassen gesamt", 38) and a coloured dot for what kind of figure it is. `valueTestId` marks the number for tests. */
@Component({
  selector: 'wghn-stat-card',
  host: { class: 'block rounded-2xl border border-outline-variant bg-surface-container-low px-5 py-4' },
  template: `
    <div class="flex items-center gap-2 text-on-surface-variant">
      <span class="inline-block size-2.5 rounded-full" [class]="dot()" aria-hidden="true"></span>{{ label() }}
    </div>
    <div class="mt-2 text-4xl font-semibold" [attr.data-testid]="valueTestId()">{{ value() }}</div>
  `,
})
export class WghnStatCard {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly tone = input<'neutral' | 'ok' | 'attention'>('neutral');
  readonly valueTestId = input<string | null>(null);

  protected dot(): string {
    return { neutral: 'bg-outline', ok: 'bg-primary', attention: 'bg-attention-border' }[this.tone()];
  }
}
