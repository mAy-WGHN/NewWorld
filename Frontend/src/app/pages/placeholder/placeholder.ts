import { Component, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute } from '@angular/router';

/** Stands in for an area that is not built yet; title and description come from the route data (see nav-items.ts). */
@Component({
  selector: 'app-placeholder',
  imports: [MatCardModule, MatIconModule],
  template: `
    <h1 class="m-0 mb-2 text-2xl font-medium">{{ title }}</h1>
    <p class="m-0 text-on-surface-variant">{{ about }}</p>
    <mat-card appearance="outlined" class="mt-5 max-w-xl">
      <mat-card-content class="flex items-center gap-3 !py-4 text-on-surface-variant">
        <mat-icon>construction</mat-icon>
        <span>Dieser Bereich wird noch gebaut.</span>
      </mat-card-content>
    </mat-card>
  `,
})
export class PlaceholderPage {
  private readonly data = inject(ActivatedRoute).snapshot.data;

  protected readonly title = (this.data['title'] as string | undefined) ?? '';
  protected readonly about = (this.data['about'] as string | undefined) ?? '';
}
