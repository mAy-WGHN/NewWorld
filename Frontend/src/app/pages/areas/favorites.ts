import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { FavoritesService } from '../../core/favorites.service';
import { visibleItems } from '../../core/nav/nav-items';

/** The areas this person pinned. Areas they may no longer open do not show up. */
@Component({
  selector: 'app-favorites',
  imports: [RouterLink],
  template: `
    <h1>Favoriten</h1>
    @if (items().length) {
      <ul class="list">
        @for (item of items(); track item.path) {
          <li>
            <a [routerLink]="'/' + item.path"><strong>{{ item.label }}</strong><span>{{ item.about }}</span></a>
          </li>
        }
      </ul>
    } @else {
      <p class="muted">Noch keine Favoriten. Unter <a routerLink="/areas">Alle Bereiche</a> markierst du einen Bereich mit dem Stern.</p>
    }
  `,
  styles: `
    h1 { margin: 0 0 20px; font-size: 26px; }
    .muted { color: var(--muted); }
    .list { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 14px; }
    a { color: var(--text); text-decoration: none; }
    li a { display: flex; flex-direction: column; gap: 4px; padding: 16px 18px; background: var(--surface); border: 1px solid var(--border); border-radius: 14px; box-shadow: var(--shadow); }
    li a:hover { border-color: var(--accent); }
    li span { color: var(--muted); font-size: 13px; }
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
