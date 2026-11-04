import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { visibleNav } from '../../core/nav/nav-items';

/** Start page: what this person may open and which privileges they hold (grouped by area). */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardPage {
  private readonly auth = inject(AuthService);

  protected readonly roles = this.auth.roles;
  protected readonly areas = computed(() => visibleNav(this.auth.privileges()));
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
