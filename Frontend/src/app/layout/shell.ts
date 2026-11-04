import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { visibleNav } from '../core/nav/nav-items';
import { NavigationState, PageReloader } from '../core/navigation-state.service';
import { NotificationsService } from '../core/notifications.service';
import { ProfileService } from '../core/profile.service';
import { SearchBox } from './search-box';

const REFRESH_MS = 60_000;

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
  protected readonly navigation = inject(NavigationState);

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
    void this.profile.load();
    void this.notifications.refresh();
    const timer = setInterval(() => void this.notifications.refresh(), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
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
