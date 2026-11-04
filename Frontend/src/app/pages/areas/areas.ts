import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { FavoritesService } from '../../core/favorites.service';
import { visibleNav } from '../../core/nav/nav-items';
import { IconComponent } from '../../shared/icon';

/** Every area this person may open, grouped, with a star to pin it; below the own privileges. */
@Component({
  selector: 'app-areas',
  imports: [RouterLink, IconComponent],
  templateUrl: './areas.html',
  styleUrl: './areas.scss',
})
export class AreasPage {
  private readonly auth = inject(AuthService);
  protected readonly favorites = inject(FavoritesService);

  protected readonly groups = computed(() => visibleNav(this.auth.privileges()));
  protected readonly roles = this.auth.roles;
  protected readonly privilegeCount = computed(() => this.auth.privileges().grants.length);

  /** Privileges grouped by their first key segment ("absence", "employee", ...). */
  protected readonly byArea = computed(() => {
    const groups = new Map<string, string[]>();
    for (const grant of this.auth.privileges().grants) {
      const area = grant.key.split(':')[0];
      groups.set(area, [...(groups.get(area) ?? []), grant.key]);
    }
    return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b));
  });
}
