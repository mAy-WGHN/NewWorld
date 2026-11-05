import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { NotificationItem } from '../features/notifications/notification-models';
import { AuthService } from './auth/auth.service';
import { ConfigService } from './config';

/** Notifications that came in since the last look; `id` changes with every arrival so a watcher notices equal content. */
export interface Arrival {
  readonly id: number;
  readonly items: readonly NotificationItem[];
}

/** At most this many new notifications are shown at once (the rest is in the list). */
const MAX_SHOWN = 3;

/** How many notifications the person has not read yet (the badge at the bell) and which ones just arrived (the snackbar). */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly auth = inject(AuthService);
  private arrivals = 0;

  /** `null` until known (or without the privilege). */
  readonly unread = signal<number | null>(null);

  /** The newest arrival; `null` until something comes in after the first count (the first count never announces anything). */
  readonly arrived = signal<Arrival | null>(null);

  async refresh(): Promise<void> {
    if (!this.auth.privileges().has('notification:read-own')) {
      return;
    }
    try {
      const answer = await firstValueFrom(this.http.get<{ count: number }>(this.config.url('notifications', '/api/me/notifications/unread-count')));
      const before = this.unread();
      this.unread.set(answer.count);
      if (before !== null && answer.count > before) {
        await this.announce(Math.min(answer.count - before, MAX_SHOWN));
      }
    } catch {
      // keep the last count; the next refresh tries again
    }
  }

  clear(): void {
    this.unread.set(null);
    this.arrived.set(null);
  }

  private async announce(take: number): Promise<void> {
    const params = new HttpParams().set('take', take);
    const newest = await firstValueFrom(this.http.get<NotificationItem[]>(this.config.url('notifications', '/api/me/notifications'), { params }));
    const unread = newest.filter((n) => !n.readAt);
    if (unread.length > 0) {
      this.arrived.set({ id: ++this.arrivals, items: unread });
    }
  }
}
