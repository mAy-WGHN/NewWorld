import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

/** Stands in for an area that is not built yet; title and description come from the route data (see nav-items.ts). */
@Component({
  selector: 'app-placeholder',
  template: `
    <h1>{{ title }}</h1>
    <p class="muted">{{ about }}</p>
    <p class="note">Dieser Bereich wird noch gebaut.</p>
  `,
  styles: `
    h1 { margin: 0 0 8px; font-size: 26px; }
    .muted { color: var(--muted); }
    .note { margin-top: 20px; padding: 12px 14px; border: 1px dashed var(--border); border-radius: 10px; color: var(--muted); }
  `,
})
export class PlaceholderPage {
  private readonly data = inject(ActivatedRoute).snapshot.data;

  protected readonly title = (this.data['title'] as string | undefined) ?? '';
  protected readonly about = (this.data['about'] as string | undefined) ?? '';
}
