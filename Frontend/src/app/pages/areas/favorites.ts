import { Component, computed, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { FavoritesService } from '../../core/favorites.service';
import { visibleItems } from '../../core/nav/nav-items';
import { PageHead } from '../../shared/ui/page-head';

/** The areas this person pinned. Areas they may no longer open do not show up. */
@Component({
  selector: 'app-favorites',
  imports: [MatCardModule, PageHead, RouterLink],
  template: `
    <app-page-head title="Favoriten" />
    @if (items().length) {
      <div class="grid grid-cols-[repeat(auto-fill,minmax(240px,1fr))] gap-3.5">
        @for (item of items(); track item.path) {
          <mat-card appearance="outlined">
            <a class="flex flex-col gap-1 px-4.5 py-4 text-on-surface no-underline hover:bg-surface-container" [routerLink]="'/' + item.path">
              <strong>{{ item.label }}</strong>
              <span class="text-[13px] text-on-surface-variant">{{ item.about }}</span>
            </a>
          </mat-card>
        }
      </div>
    } @else {
      <p class="m-0 text-on-surface-variant">Noch keine Favoriten. Unter <a routerLink="/areas">Alle Bereiche</a> markierst du einen Bereich mit dem Stern.</p>
    }
  `,
})
export class FavoritesPage {
  private readonly auth = inject(AuthService);
  private readonly favorites = inject(FavoritesService);

  protected readonly items = computed(() => {
    const allowed = visibleItems(this.auth.privileges());
    return this.favorites.paths().flatMap((path) => allowed.filter((i) => i.path === path));
  });
}
