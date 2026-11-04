import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { NotificationsService } from '../core/notifications.service';
import { ProfileService } from '../core/profile.service';
import { IconComponent } from '../shared/icon';
import { SearchBox } from './search-box';

const REFRESH_MS = 60_000;

/** The frame around every signed-in page: header with search, bell and person; slim side bar; content. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent, SearchBox],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly profile = inject(ProfileService);
  private readonly notifications = inject(NotificationsService);

  protected readonly menuOpen = signal(false);
  protected readonly displayName = this.profile.displayName;
  protected readonly initials = this.profile.initials;
  protected readonly personnelNumber = this.profile.personnelNumber;
  protected readonly unread = this.notifications.unread;

  protected readonly canSeeNotifications = computed(() => this.auth.privileges().has('notification:read-own'));

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

  protected async logout(): Promise<void> {
    await this.auth.logout();
    this.profile.clear();
    this.notifications.clear();
    await this.router.navigate(['/login']);
  }
}
