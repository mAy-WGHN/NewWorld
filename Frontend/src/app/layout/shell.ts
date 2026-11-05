import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { grantsOfRole } from '../core/auth/privileges';
import { AdminAccessApi } from '../features/admin-access/admin-access.api';
import { Role } from '../features/admin-access/admin-access-models';
import { visibleNav } from '../core/nav/nav-items';
import { NavigationState, PageReloader } from '../core/navigation-state.service';
import { NotificationsService } from '../core/notifications.service';
import { notificationTarget } from '../features/notifications/notification-link';
import { NotificationItem } from '../features/notifications/notification-models';
import { ProfileService } from '../core/profile.service';
import { SearchBox } from './search-box';

/** How often the count is asked for; a new notification shows up in the snackbar within this time. */
const REFRESH_MS = 15_000;
/** How long the snackbar stays. */
const SNACKBAR_MS = 8_000;

/** Below this width the side bar is a drawer that opens over the page. */
const SMALL_SCREEN = '(max-width: 1000px)';

/** The frame around every signed-in page: toolbar with search, bell and person; side bar with the areas; content. */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatBadgeModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    MatMenuModule,
    MatProgressBarModule,
    MatSidenavModule,
    MatSnackBarModule,
    MatToolbarModule,
    SearchBox,
  ],
  templateUrl: './shell.html',
})
export class Shell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly profile = inject(ProfileService);
  private readonly notifications = inject(NotificationsService);
  private readonly reloader = inject(PageReloader);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly navigation = inject(NavigationState);
  private readonly accessApi = inject(AdminAccessApi);

  /** The role whose view is previewed, or null. */
  protected readonly previewRole = this.auth.previewRole;
  /** The preview needs the real right to read roles and the catalogue (the preview itself changes nothing). */
  protected readonly canPreview = computed(() => this.auth.realPrivileges().has('access:role:read') && this.auth.realPrivileges().has('access:catalog:read'));
  protected readonly roles = signal<Role[]>([]);
  protected readonly previewError = signal<string | null>(null);
  private catalogKeys: string[] | null = null;

  /** Loads the roles when the menu opens, so the list is current. */
  protected async loadRoles(): Promise<void> {
    this.previewError.set(null);
    try {
      const [roles, catalog] = await Promise.all([this.accessApi.roles(), this.catalogKeys ? null : this.accessApi.catalog()]);
      this.roles.set([...roles].sort((a, b) => a.name.localeCompare(b.name, 'de')));
      this.catalogKeys ??= catalog?.map((c) => c.key) ?? [];
    } catch {
      this.previewError.set('Die Rollen konnten nicht geladen werden.');
    }
  }

  protected async previewAs(role: Role): Promise<void> {
    if (!this.catalogKeys) {
      await this.loadRoles();
    }
    if (this.catalogKeys) {
      this.auth.startPreview(role.name, grantsOfRole(role.privileges, this.catalogKeys));
      await this.router.navigateByUrl('/');
    }
  }

  protected endPreview(): void {
    this.auth.endPreview();
  }

  protected readonly small = toSignal(inject(BreakpointObserver).observe(SMALL_SCREEN).pipe(map((state) => state.matches)), { initialValue: false });
  protected readonly menuOpen = signal(false);
  protected readonly displayName = this.profile.displayName;
  protected readonly initials = this.profile.initials;
  protected readonly personnelNumber = this.profile.personnelNumber;
  protected readonly unread = this.notifications.unread;
  /** The number at the bell; big numbers are cut off. */
  protected readonly badge = computed(() => ((this.unread() ?? 0) > 99 ? '99+' : String(this.unread() ?? 0)));

  protected readonly canSeeNotifications = computed(() => this.auth.privileges().has('notification:read-own'));

  /** The areas of the person, grouped (the side bar lists them so that every one is one click away). */
  protected readonly areas = computed(() => visibleNav(this.auth.privileges()));

  constructor() {
    effect(() => {
      const arrival = this.notifications.arrived();
      if (arrival) {
        this.announce(arrival.items);
      }
    });
    void this.profile.load();
    void this.notifications.refresh();
    const timer = setInterval(() => void this.notifications.refresh(), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  /** Highlights new notifications in a snackbar; one is shown with its title and text, several as a count. */
  private announce(items: readonly NotificationItem[]): void {
    const first = items[0];
    const message = items.length === 1 ? `${first.title}: ${first.body}` : `${items.length} neue Mitteilungen`;
    const ref = this.snackBar.open(message, 'Ansehen', { duration: SNACKBAR_MS, horizontalPosition: 'end', verticalPosition: 'bottom', panelClass: 'notification-snackbar' });
    ref.onAction().subscribe(() => void this.router.navigateByUrl(items.length === 1 ? notificationTarget(first, this.auth.privileges()) : '/notifications'));
  }

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
  }

  protected reload(): void {
    this.reloader.reloadNow();
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
    this.profile.clear();
    this.notifications.clear();
    await this.router.navigate(['/login']);
  }
}
